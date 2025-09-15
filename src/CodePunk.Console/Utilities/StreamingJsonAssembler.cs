using System;
using System.Text;
using System.Text.Json;

namespace CodePunk.Console.Utilities;

/// <summary>
/// Incremental streaming JSON assembler. Accepts UTF-8 byte chunks and tries
/// to parse a single top-level JSON value using <see cref="Utf8JsonReader"/>.
/// It tolerates multibyte UTF-8 sequences split across chunks and enforces a
/// maximum buffer size.
/// </summary>
public sealed class StreamingJsonAssembler
{
    private readonly int _maxBytes;
    private byte[] _buffer;
    private int _written;
    private bool _hasOverflowed;

    public StreamingJsonAssembler(int maxBufferBytes = 256 * 1024)
    {
        _maxBytes = maxBufferBytes;
        _buffer = new byte[Math.Min(4096, maxBufferBytes)];
        _written = 0;
    }

    public int CurrentLength => _written;
    public int MaxBufferBytes => _maxBytes;
    public bool HasOverflowed => _hasOverflowed;

    public void Reset()
    {
        _written = 0;
        _hasOverflowed = false;
    }

    public void Append(string chunk)
    {
        if (string.IsNullOrEmpty(chunk)) return;
        var bytes = Encoding.UTF8.GetBytes(chunk);
        Append(bytes.AsSpan());
    }

    public void Append(ReadOnlySpan<byte> bytes)
    {
        if (_hasOverflowed) return;
        if (_written + bytes.Length > _maxBytes)
        {
            _hasOverflowed = true;
            return;
        }
        EnsureCapacity(_written + bytes.Length);
        bytes.CopyTo(new Span<byte>(_buffer, _written, bytes.Length));
        _written += bytes.Length;
    }

    public void Append(ReadOnlyMemory<byte> bytes) => Append(bytes.Span);

    public bool TryGetNext(out JsonElement element, out string raw, out string? diagnostic)
    {
        element = default;
        raw = string.Empty;
        diagnostic = null;

        if (_written == 0) return false;

        var span = new ReadOnlySpan<byte>(_buffer, 0, _written);
        // First, detect SSE-style events terminated by a blank line and assemble
        // the 'data:' lines into a single JSON payload if present.
        int sseTermPos = IndexOfDoubleNewline(span);
        if (sseTermPos >= 0)
        {
            int termLen = DetectTerminatorLength(span, sseTermPos);
            // slice of the event (excluding the terminator)
            var eventSpan = span.Slice(0, sseTermPos);
            // collect all data: lines
            using var ms = new System.IO.MemoryStream();
            int i = 0;
            while (i < eventSpan.Length)
            {
                // read line until \n
                int lineStart = i;
                int lineEnd = i;
                while (lineEnd < eventSpan.Length && eventSpan[lineEnd] != (byte)'\n') lineEnd++;
                int len = lineEnd - lineStart;
                // trim trailing \r
                if (len > 0 && eventSpan[lineEnd - 1] == (byte)'\r') len--;
                // check for 'data:' prefix
                if (len >= 5 && eventSpan[lineStart] == (byte)'d' && eventSpan[lineStart + 1] == (byte)'a' && eventSpan[lineStart + 2] == (byte)'t' && eventSpan[lineStart + 3] == (byte)'a' && eventSpan[lineStart + 4] == (byte)':')
                {
                    int payloadStart = lineStart + 5;
                    // skip a single space after colon
                    if (payloadStart < lineStart + len && eventSpan[payloadStart] == (byte)' ') payloadStart++;
                    int payloadLen = lineStart + len - payloadStart;
                    if (payloadLen > 0)
                    {
                        ms.Write(eventSpan.Slice(payloadStart, payloadLen));
                    }
                }
                i = lineEnd + 1; // move past newline
            }

            var combined = ms.ToArray();
            if (combined.Length > 0)
            {
                try
                {
                    using var doc = JsonDocument.Parse(combined);
                    element = doc.RootElement.Clone();
                    raw = Encoding.UTF8.GetString(combined);
                    // consume the event bytes including terminator
                    int consume = sseTermPos + termLen;
                    var remaining = _written - consume;
                    if (remaining > 0) Buffer.BlockCopy(_buffer, consume, _buffer, 0, remaining);
                    _written = remaining;
                    diagnostic = null;
                    return true;
                }
                catch (JsonException je)
                {
                    diagnostic = "sse-json-parse-failed: " + je.Message;
                    // consume the event and continue (drop malformed)
                    int consume = sseTermPos + termLen;
                    var remaining = _written - consume;
                    if (remaining > 0) Buffer.BlockCopy(_buffer, consume, _buffer, 0, remaining);
                    _written = remaining;
                    return false;
                }
            }
            // if no combined payload, drop the event
            int drop = sseTermPos + termLen;
            var rem = _written - drop;
            if (rem > 0) Buffer.BlockCopy(_buffer, drop, _buffer, 0, rem);
            _written = rem;
            // continue to normal processing
        }

        // Try to find a JSON start within the buffer (skip SSE framing and other junk)
        int start = IndexOfJsonStart(span, 0);
        if (start < 0)
        {
            diagnostic = "no-json-start";
            return false;
        }

        // Attempt parsing starting at each candidate start position. If a parse throws
        // JsonException, advance to the next candidate and retry. If reader indicates
        // incomplete, return incomplete so caller can append more bytes.
        Exception? lastEx = null;
        while (start >= 0 && start < span.Length)
        {
            var attemptSpan = span.Slice(start);
            // quick heuristic: if it starts with object/array ensure a matching
            // closing bracket exists in the buffer before attempting to parse.
            if (attemptSpan.Length > 0)
            {
                var first = attemptSpan[0];
                if (first == (byte)'{' && !attemptSpan.Contains((byte)'}'))
                {
                    diagnostic = "incomplete";
                    return false;
                }
                if (first == (byte)'[' && !attemptSpan.Contains((byte)']'))
                {
                    diagnostic = "incomplete";
                    return false;
                }
            }
            var reader = new Utf8JsonReader(attemptSpan, isFinalBlock: true, state: default);
            try
            {
                if (!reader.Read())
                {
                    diagnostic = "incomplete";
                    return false;
                }

                if (reader.TokenType == JsonTokenType.StartObject || reader.TokenType == JsonTokenType.StartArray)
                {
                    int depth = 0;
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonTokenType.StartObject || reader.TokenType == JsonTokenType.StartArray)
                        {
                            depth++;
                            continue;
                        }
                        if (reader.TokenType == JsonTokenType.EndObject || reader.TokenType == JsonTokenType.EndArray)
                        {
                            if (depth == 0)
                            {
                                var consumed = (int)reader.BytesConsumed;
                                var consumedTotal = start + consumed;
                                raw = Encoding.UTF8.GetString(_buffer, start, consumed);
                                using var doc = JsonDocument.Parse(raw);
                                // Ensure the parsed root kind matches the initial byte we started at.
                                if (!JsonKindMatchesStart(doc.RootElement.ValueKind, attemptSpan[0]))
                                {
                                    // treat as malformed and continue searching
                                    lastEx = new JsonException("mismatched-root-kind");
                                    start = IndexOfJsonStart(span, start + 1);
                                    continue;
                                }
                                element = doc.RootElement.Clone();
                                var remaining = _written - consumedTotal;
                                if (remaining > 0) Buffer.BlockCopy(_buffer, consumedTotal, _buffer, 0, remaining);
                                _written = remaining;
                                return true;
                            }
                            depth--;
                        }
                    }
                    diagnostic = "incomplete";
                    return false;
                }

                // Primitive root value
                var consumedPrim = (int)reader.BytesConsumed;
                var consumedPrimTotal = start + consumedPrim;
                raw = Encoding.UTF8.GetString(_buffer, start, consumedPrim);
                using var docPrim = JsonDocument.Parse(raw);
                if (!JsonKindMatchesStart(docPrim.RootElement.ValueKind, attemptSpan[0]))
                {
                    lastEx = new JsonException("mismatched-root-kind");
                    start = IndexOfJsonStart(span, start + 1);
                    continue;
                }
                element = docPrim.RootElement.Clone();
                var remainingPrim = _written - consumedPrimTotal;
                if (remainingPrim > 0) Buffer.BlockCopy(_buffer, consumedPrimTotal, _buffer, 0, remainingPrim);
                _written = remainingPrim;
                return true;
            }
            catch (JsonException jex)
            {
                lastEx = jex;
                // advance to next '{' or '[' after this start
                start = IndexOfJsonStart(span, start + 1);
                continue;
            }
            catch (Exception ex)
            {
                lastEx = ex;
                break;
            }
        }

        diagnostic = lastEx?.Message ?? "no-valid-json-found";
        return false;
    }

    private static int IndexOfJsonStart(ReadOnlySpan<byte> span, int offset)
    {
        for (int i = offset; i < span.Length; i++)
        {
            var b = span[i];
            // skip common whitespace
            if (b == (byte)' ' || b == (byte)'\n' || b == (byte)'\r' || b == (byte)'\t') continue;
            // JSON object/array start
            if (b == (byte)'{' || b == (byte)'[') return i;
            // string/primitive starts: '"', digit, '-', 't','f','n'
            if (b == (byte)'"' || (b >= (byte)'0' && b <= (byte)'9') || b == (byte)'-' || b == (byte)'t' || b == (byte)'f' || b == (byte)'n') return i;
            // otherwise continue scanning (this will skip SSE 'data:' prefixes and other junk)
        }
        return -1;
    }

    private static bool JsonKindMatchesStart(JsonValueKind kind, byte startByte)
    {
        return (startByte) switch
        {
            (byte)'{' => kind == JsonValueKind.Object,
            (byte)'[' => kind == JsonValueKind.Array,
            (byte)'"' => kind == JsonValueKind.String,
            (byte)'-' => kind == JsonValueKind.Number,
            var b when (b >= (byte)'0' && b <= (byte)'9') => kind == JsonValueKind.Number,
            (byte)'t' or (byte)'f' => kind == JsonValueKind.True || kind == JsonValueKind.False,
            (byte)'n' => kind == JsonValueKind.Null,
            _ => false,
        };
    }

    private static int IndexOfDoubleNewline(ReadOnlySpan<byte> span)
    {
        for (int i = 0; i + 1 < span.Length; i++)
        {
            if (span[i] == (byte)'\n' && span[i + 1] == (byte)'\n') return i + 2 > span.Length ? span.Length - 2 : i + 2 - 2; // return index of first blank line start
            if (span[i] == (byte)'\r' && i + 3 < span.Length && span[i + 1] == (byte)'\n' && span[i + 2] == (byte)'\r' && span[i + 3] == (byte)'\n') return i + 4 - 2;
        }
        // Also handle CRLF + CRLF sequences by searching for "\r\n\r\n"
        for (int i = 0; i + 3 < span.Length; i++)
        {
            if (span[i] == (byte)'\r' && span[i + 1] == (byte)'\n' && span[i + 2] == (byte)'\r' && span[i + 3] == (byte)'\n') return i + 4;
        }
        return -1;
    }

    private static int DetectTerminatorLength(ReadOnlySpan<byte> span, int index)
    {
        // naive: check if preceding characters ended with CRLF sequences
        if (index >= 4 && span[index - 4] == (byte)'\r' && span[index - 3] == (byte)'\n' && span[index - 2] == (byte)'\r' && span[index - 1] == (byte)'\n') return 4;
        if (index >= 2 && span[index - 2] == (byte)'\n' && span[index - 1] == (byte)'\n') return 2;
        return 2;
    }

    public byte[] DebugSnapshot()
    {
        var copy = new byte[_written];
        if (_written > 0) Buffer.BlockCopy(_buffer, 0, copy, 0, _written);
        return copy;
    }

    private void EnsureCapacity(int min)
    {
        if (_buffer.Length >= min) return;
        int newSize = _buffer.Length == 0 ? 4096 : _buffer.Length * 2;
        while (newSize < min) newSize *= 2;
        if (newSize > _maxBytes) newSize = _maxBytes;
        var newBuf = new byte[newSize];
        if (_written > 0) Buffer.BlockCopy(_buffer, 0, newBuf, 0, _written);
        _buffer = newBuf;
    }
}
