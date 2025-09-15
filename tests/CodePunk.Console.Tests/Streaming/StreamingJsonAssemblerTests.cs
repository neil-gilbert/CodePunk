using System.Text;
using System.Text.Json;
using CodePunk.Console.Utilities;
using Xunit;

namespace CodePunk.Console.Tests.Streaming;

public class StreamingJsonAssemblerTests
{
    [Fact]
    public void AssemblesJsonSplitAcrossChunks()
    {
        var assembler = new StreamingJsonAssembler(1024);
        var json = "{\"files\":[{\"path\":\"README.md\",\"rationale\":\"hello\"}]}";
        // split at arbitrary byte boundary
        var mid = json.Length / 3;
        var a = json.Substring(0, mid);
        var b = json.Substring(mid, json.Length - mid);
        assembler.Append(a);
        Assert.False(assembler.TryGetNext(out _, out _, out _));
        assembler.Append(b);
        Assert.True(assembler.TryGetNext(out var el, out var raw, out var diag));
        Assert.NotNull(el);
        Assert.Equal("{\"files\":[{\"path\":\"README.md\",\"rationale\":\"hello\"}]}", raw);
        Assert.Null(diag);
    }

    [Fact]
    public void HandlesSseDataLines()
    {
        var assembler = new StreamingJsonAssembler(1024);
        // Simulate SSE payload where each data: line contains a JSON fragment
        var json = "{\"files\":[{\"path\":\"sse.txt\",\"rationale\":\"sse\"}]}";
        var part1 = "data: " + json.Substring(0, json.Length/2) + "\n";
        var part2 = "data: " + json.Substring(json.Length/2) + "\n\n"; // terminated event
        // Append raw bytes as provider would stream them
    assembler.Append(Encoding.UTF8.GetBytes(part1).AsSpan());
        Assert.False(assembler.TryGetNext(out _, out _, out _));
    assembler.Append(Encoding.UTF8.GetBytes(part2).AsSpan());
        // assembler should ignore the SSE framing and assemble the JSON
        Assert.True(assembler.TryGetNext(out var elSse, out var rawSse, out var diagSse));
        Assert.Null(diagSse);
        Assert.Equal("sse.txt", elSse.GetProperty("files").EnumerateArray().First().GetProperty("path").GetString());
    }

    [Fact]
    public void RecoversFromMalformedThenValid()
    {
        var assembler = new StreamingJsonAssembler(1024);
        // Send a malformed prefix, then a valid JSON
        var bad = "{ this is not json ";
        var good = "{\"files\":[{\"path\":\"ok.txt\",\"rationale\":\"ok\"}]}";
    assembler.Append(Encoding.UTF8.GetBytes(bad).AsSpan());
        Assert.False(assembler.TryGetNext(out _, out _, out var diag1));
    assembler.Append(Encoding.UTF8.GetBytes(good).AsSpan());
        // After appending the valid JSON, assembler should be able to parse the next top-level JSON
        Assert.True(assembler.TryGetNext(out var elGood, out var rawGood, out var diagGood));
        Assert.Null(diagGood);
        Assert.Equal("ok.txt", elGood.GetProperty("files").EnumerateArray().First().GetProperty("path").GetString());
    }

    [Fact]
    public void RespectsBufferOverflow()
    {
        var max = 32;
        var assembler = new StreamingJsonAssembler(max);
        // create a payload larger than max
        var large = new string('A', max + 10);
        var json = "{\"files\":[{\"path\":\"big.txt\",\"rationale\":\"" + large + "\"}]}";
        var bytes = Encoding.UTF8.GetBytes(json);
    assembler.Append(bytes.AsSpan());
        Assert.True(assembler.HasOverflowed);
        Assert.False(assembler.TryGetNext(out _, out _, out _));
    }

    [Fact]
    public void HandlesMultibyteUtf8Split()
    {
        var assembler = new StreamingJsonAssembler(2048);
        var rationale = "Fix the issue 💡 in code"; // emoji is multibyte
        var payload = new { files = new[] { new { path = "a.txt", rationale } } };
        var json = JsonSerializer.Serialize(payload);
        // force a split inside the emoji by slicing bytes
        var bytes = Encoding.UTF8.GetBytes(json);
        var emojiUtf8 = Encoding.UTF8.GetBytes("💡");
        // find emoji byte index by searching the bytes for the emoji sequence
        int idx = -1;
        for (int i = 0; i <= bytes.Length - emojiUtf8.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < emojiUtf8.Length; j++) { if (bytes[i + j] != emojiUtf8[j]) { match = false; break; } }
            if (match) { idx = i; break; }
        }
        // If the serializer escaped the emoji into \uXXXX sequences, look for that byte sequence instead.
        if (idx < 0)
        {
            var sur = "💡".ToCharArray();
            if (sur.Length == 2)
            {
                var esc = $"\\u{(int)sur[0]:X4}\\u{(int)sur[1]:X4}";
                var escBytes = Encoding.UTF8.GetBytes(esc);
                for (int i = 0; i <= bytes.Length - escBytes.Length; i++)
                {
                    bool match = true;
                    for (int j = 0; j < escBytes.Length; j++) { if (bytes[i + j] != escBytes[j]) { match = false; break; } }
                    if (match) { idx = i; break; }
                }
            }
        }
        Assert.True(idx >= 0);
        // split inside the emoji bytes (or escape sequence) to simulate a chunk boundary
        var split = idx + 1;
        var part1 = new ReadOnlyMemory<byte>(bytes, 0, split);
        var part2 = new ReadOnlyMemory<byte>(bytes, split, bytes.Length - split);
        // sanity-check: the entire bytes should parse as JSON
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        }
        catch (Exception ex)
        {
            throw new Xunit.Sdk.XunitException($"Full-bytes parse failed: {ex.Message}");
        }
        assembler.Append(part1);
        Assert.False(assembler.TryGetNext(out _, out _, out _));
    assembler.Append(part2);
    // verify the assembler buffer contains exactly the bytes we expect
    var snapshot = assembler.DebugSnapshot();
    Assert.Equal(bytes, snapshot);

    var ok = assembler.TryGetNext(out var el, out var raw, out var diag);
        if (!ok)
        {
            System.Console.WriteLine($"DIAG: [{diag}] bufferLen={assembler.CurrentLength} rawPreview=[{raw?.Substring(0, Math.Min(200, raw?.Length ?? 0))}]");
            var snap = assembler.DebugSnapshot();
            System.Console.WriteLine($"BUFFER({snap.Length}): {Encoding.UTF8.GetString(snap)}");
            // also verify the original bytes are parseable
            try
            {
                using var doc = JsonDocument.Parse(bytes);
                System.Console.WriteLine($"Full-bytes parse succeeded. RootKind={doc.RootElement.ValueKind}");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"Full-bytes parse failed: {ex.Message}");
            }
        }
        Assert.True(ok, $"TryGetNext failed. diag=[{diag}] bufferLen={assembler.CurrentLength}");
        Assert.Equal(JsonValueKind.Object, el.ValueKind);
        var files = el.GetProperty("files").EnumerateArray().ToArray();
        Assert.Single(files);
        Assert.Equal(rationale, files[0].GetProperty("rationale").GetString());
    }
}
