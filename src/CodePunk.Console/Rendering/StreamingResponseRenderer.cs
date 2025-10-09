using CodePunk.Core.Chat;
using CodePunk.Core.Models;
using CodePunk.Core.Extensions;
using CodePunk.Core.SyntaxHighlighting;
using CodePunk.Core.SyntaxHighlighting.Abstractions;
using CodePunk.Console.SyntaxHighlighting;
using Spectre.Console;
using System.Collections.Generic;
using System.Text;
using CodePunk.Console.Themes;

namespace CodePunk.Console.Rendering;

/// <summary>
/// Renders streaming AI responses (baseline). Writes chunks directly; later we can upgrade to Live panels.
/// </summary>
public class StreamingResponseRenderer
{
    private readonly IAnsiConsole _console;
    private readonly ISyntaxHighlighter? _syntaxHighlighter;
    private readonly StringBuilder _buffer = new();
    private bool _isStreaming;
    private DateTime _startUtc;
    private readonly StreamingRendererOptions _options;
    private CancellationTokenSource? _liveCts;
    private Task? _liveTask;
    private readonly object _sync = new();
    private int? _inputTokens;
    private int? _outputTokens;
    private decimal? _estimatedCost;
    private readonly Dictionary<string, ToolCallPart> _toolCallCache = new();
    private const int ToolResultPreviewLines = 20;

    public StreamingResponseRenderer(
        IAnsiConsole console,
        StreamingRendererOptions? options = null,
        ISyntaxHighlighter? syntaxHighlighter = null)
    {
        _console = console;
        _options = options ?? new StreamingRendererOptions();
        _syntaxHighlighter = syntaxHighlighter;
    }

    public void StartStreaming()
    {
        _buffer.Clear();
        _isStreaming = true;
        _startUtc = DateTime.UtcNow;
        _console.Write(new Rule().LeftJustified());
        var header = _options.LiveEnabled ? "CodePunk Assistant (live)" : "CodePunk Assistant";
        _console.MarkupLine(ConsoleStyles.Accent(header));
        _console.WriteLine();

        if (_options.LiveEnabled)
        {
            _liveCts = new CancellationTokenSource();
            var token = _liveCts.Token;
            _liveTask = Task.Run(() =>
            {
                try
                {
                    _console.Live(BuildPanel(string.Empty))
                        .AutoClear(false)
                        .Start(ctx =>
                        {
                            while (!token.IsCancellationRequested)
                            {
                                string snapshot;
                                TimeSpan elapsed;
                                lock (_sync)
                                {
                                    snapshot = _buffer.ToString();
                                    elapsed = DateTime.UtcNow - _startUtc;
                                }
                                ctx.UpdateTarget(BuildPanel(snapshot, elapsed));
                                ctx.Refresh();
                                Thread.Sleep(100); // ~10fps
                            }

                            string finalSnapshot;
                            TimeSpan finalElapsed;
                            lock (_sync)
                            {
                                finalSnapshot = _buffer.ToString();
                                finalElapsed = DateTime.UtcNow - _startUtc;
                            }
                            ctx.UpdateTarget(BuildPanel(finalSnapshot, finalElapsed, completed: true));
                            ctx.Refresh();
                        });
                }
                catch { /* swallow best-effort */ }
            }, token);
        }
    }

    public void ProcessChunk(ChatStreamChunk chunk)
    {
        if (!_isStreaming) return;

        if (ToolStatusSerializer.TryDeserialize(chunk.ContentDelta, out var statusPayload) && statusPayload != null)
        {
            lock (_sync)
            {
                AppendStatusToBuffer(statusPayload);
                UpdateUsageFromChunk(chunk);
            }

            RenderToolStatusPayload(statusPayload);
            return;
        }

        if (string.IsNullOrEmpty(chunk.ContentDelta))
        {
            lock (_sync)
            {
                UpdateUsageFromChunk(chunk);
            }
            return;
        }

        lock (_sync)
        {
            _buffer.Append(chunk.ContentDelta);
            UpdateUsageFromChunk(chunk);
        }
        if (!_options.LiveEnabled)
        {
            _console.Markup(ConsoleStyles.Escape(chunk.ContentDelta));
        }
    }

    public void CompleteStreaming()
    {
        if (!_isStreaming) return;
        _isStreaming = false;
        if (_options.LiveEnabled)
        {
            try
            {
                _liveCts?.Cancel();
                _liveTask?.Wait(500);
            }
            catch { }
            finally
            {
                _liveCts?.Dispose();
                _liveCts = null;
                _liveTask = null;
            }
            _console.WriteLine();
        }
        else
        {
            _console.WriteLine();
        }
        var elapsed = DateTime.UtcNow - _startUtc;
        var completionLine = new StringBuilder();
        completionLine.Append($"Completed in {elapsed.TotalSeconds:F1}s");
        if (_inputTokens.HasValue || _outputTokens.HasValue)
        {
            completionLine.Append(" • tokens: ");
            var input = _inputTokens?.ToString() ?? "?";
            var output = _outputTokens?.ToString() ?? "?";
            completionLine.Append($"in {input} / out {output}");
            var total = (_inputTokens ?? 0) + (_outputTokens ?? 0);
            completionLine.Append($" (total {total}");
            if (_estimatedCost.HasValue)
            {
                completionLine.Append($", cost ~{_estimatedCost.Value:C}" );
            }
            completionLine.Append(')');
        }
        _console.MarkupLine(ConsoleStyles.Dim(completionLine.ToString()));
        _console.WriteLine();
    }

    public void RenderMessage(Message message)
    {
        _console.Write(new Rule().LeftJustified());
        var (roleColor, roleIcon) = message.Role switch
        {
            MessageRole.User => ("green", "👤"),
            MessageRole.Assistant => ("blue", "🤖"),
            MessageRole.System => ("yellow", "⚙️"),
            MessageRole.Tool => ("purple", "�"),
            _ => ("white", "💬")
        };
        _console.MarkupLine($"[bold {roleColor}]{roleIcon} {message.Role}[/]");
        if (!string.IsNullOrEmpty(message.Model))
        {
            _console.MarkupLine($"[dim]Model: {message.Model}[/]");
        }
        _console.WriteLine();
        foreach (var part in message.Parts)
        {
            RenderMessagePart(part);
        }
        _console.WriteLine();
    }

    private void RenderMessagePart(MessagePart part)
    {
        switch (part)
        {
            case TextPart text:
                RenderText(text.Content);
                break;
            case ToolCallPart toolCall:
                RenderToolCall(toolCall);
                break;
            case ToolResultPart toolResult:
                RenderToolResult(toolResult);
                break;
            case ImagePart image:
                RenderImage(image);
                break;
            default:
                _console.MarkupLine($"[dim]Unknown part: {part.GetType().Name}[/]");
                break;
        }
    }

    private void RenderText(string content)
    {
        if (string.IsNullOrEmpty(content)) return;
        var lines = content.Replace("\r\n", "\n").Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Detect code blocks
            if (line.StartsWith("```"))
            {
                var language = line.Length > 3 ? line[3..].Trim() : "";
                var codeLines = new List<string>();

                // Collect code block lines
                i++;
                while (i < lines.Length && !lines[i].StartsWith("```"))
                {
                    codeLines.Add(lines[i]);
                    i++;
                }

                RenderCodeBlock(string.Join("\n", codeLines), language);
                continue;
            }

            // Markdown rendering
            if (line.StartsWith("### "))
                _console.MarkupLine($"[bold]{ConsoleStyles.Escape(line[4..])}[/]");
            else if (line.StartsWith("## "))
                _console.MarkupLine($"[bold]{ConsoleStyles.Escape(line[3..])}[/]");
            else if (line.StartsWith("# "))
                _console.MarkupLine($"[bold underline]{ConsoleStyles.Escape(line[2..])}[/]");
            else if (line.StartsWith("- ") || line.StartsWith("* "))
                _console.MarkupLine($"  • {ConsoleStyles.Escape(line[2..])}");
            else
                _console.MarkupLine(ConsoleStyles.Escape(line));
        }
    }

    private void RenderCodeBlock(string code, string language)
    {
        if (_syntaxHighlighter != null && !string.IsNullOrWhiteSpace(language))
        {
            var renderer = new SpectreTokenRenderer(_console);
            _syntaxHighlighter.Highlight(code, language, renderer);
        }
        else
        {
            // Fallback: render as plain text with grey color
            var escaped = ConsoleStyles.Escape(code);
            _console.MarkupLine($"[grey]{escaped}[/]");
        }
    }

    private void RenderToolCall(ToolCallPart toolCall)
    {
        _toolCallCache[toolCall.Id] = toolCall;
        _console.MarkupLine("[purple]🔧 Tool Call[/]");
        _console.MarkupLine($"[dim]Id: {ConsoleStyles.Escape(toolCall.Id)}  Name: {ConsoleStyles.Escape(toolCall.Name)}[/]");
        var args = toolCall.Arguments.ToString();
        if (args.Length > 500) args = args[..500] + "…";
        _console.MarkupLine($"[dim]Args: {ConsoleStyles.Escape(args)}[/]");
        _console.WriteLine();
    }

    private void RenderToolResult(ToolResultPart toolResult)
    {
        var color = toolResult.IsError ? "red" : "green";
        var icon = toolResult.IsError ? "❌" : "✅";
        _console.MarkupLine($"[{color}]{icon} Tool Result[/]");
        _console.MarkupLine($"[dim]Tool Call ID: {ConsoleStyles.Escape(toolResult.ToolCallId)}[/]");

        if (toolResult.IsError)
        {
            var escaped = ConsoleStyles.Escape(toolResult.Content);
            _console.MarkupLine($"[red]{escaped}[/]");
            _console.WriteLine();
            return;
        }

        _toolCallCache.TryGetValue(toolResult.ToolCallId, out var toolCall);
        if (toolCall != null)
        {
            _toolCallCache.Remove(toolResult.ToolCallId);
        }
        var path = toolCall?.GetFilePath();
        if (!string.IsNullOrEmpty(path))
        {
            _console.MarkupLine($"[dim]File: {ConsoleStyles.Escape(path)}[/]");
        }

        var preview = toolResult.Content.GetLinePreview(ToolResultPreviewLines);
        if (!string.IsNullOrEmpty(preview.Preview))
        {
            var markup = BuildHighlightedMarkup(preview.Preview, path);
            _console.MarkupLine(markup);
        }

        if (preview.IsTruncated)
        {
            var footer = $"… showing first {preview.MaxLines} lines of {preview.OriginalLineCount}. Use read_file with offset/limit to view more.";
            _console.MarkupLine(ConsoleStyles.Dim(footer));
        }

        _console.WriteLine();
    }

    private void RenderImage(ImagePart image)
    {
        _console.MarkupLine($"[cyan]🖼️ Image: {ConsoleStyles.Escape(image.Url)}[/]");
        if (!string.IsNullOrEmpty(image.Description))
            _console.MarkupLine($"[dim]{ConsoleStyles.Escape(image.Description)}[/]");
        _console.WriteLine();
    }

    private Panel BuildPanel(string content, TimeSpan? elapsed = null, bool completed = false)
    {
        var body = string.IsNullOrEmpty(content)
            ? ConsoleStyles.Dim("(streaming…)")
            : ConsoleStyles.Escape(content);
        var time = elapsed.HasValue ? ConsoleStyles.Dim($" {elapsed.Value.TotalSeconds:F1}s") : string.Empty;
        var title = completed ? $"CodePunk Assistant{time}" : $"CodePunk Assistant{time}";
        return new Panel(new Markup(body))
            .Header(ConsoleStyles.PanelTitle(title))
            .RoundedBorder();
    }

    private void RenderToolStatusPayload(ToolStatusPayload payload)
    {
        var color = payload.IsError ? "red" : "green";
        var icon = payload.IsError ? "❌" : "✅";
        _console.MarkupLine($"[{color}]{icon} {payload.ToolName}[/]");

        if (!string.IsNullOrEmpty(payload.FilePath))
        {
            _console.MarkupLine($"[dim]{ConsoleStyles.Escape(payload.FilePath)}[/]");
        }

        if (!string.IsNullOrEmpty(payload.Preview))
        {
            if (payload.IsError)
            {
                _console.MarkupLine($"[red]{ConsoleStyles.Escape(payload.Preview)}[/]");
            }
            else
            {
                var markup = BuildHighlightedMarkup(payload.Preview, payload.FilePath, payload.LanguageId);
                _console.MarkupLine(markup);
            }
        }

        if (payload.IsTruncated)
        {
            var footer = payload.IsError
                ? $"… output truncated after {payload.MaxLines} lines."
                : $"… showing first {payload.MaxLines} lines of {payload.OriginalLineCount}. Use read_file with offset/limit to view more.";
            _console.MarkupLine(ConsoleStyles.Dim(footer));
        }

        _console.WriteLine();
    }

    private void AppendStatusToBuffer(ToolStatusPayload payload)
    {
        var icon = payload.IsError ? "❌" : "✅";
        _buffer.Append(icon).Append(' ').Append(payload.ToolName).AppendLine();

        if (!string.IsNullOrEmpty(payload.FilePath))
        {
            _buffer.Append(payload.FilePath).AppendLine();
        }

        if (!string.IsNullOrEmpty(payload.Preview))
        {
            _buffer.Append(payload.Preview).AppendLine();
        }

        if (payload.IsTruncated)
        {
            _buffer.Append("… (")
                .Append(payload.MaxLines)
                .Append('/')
                .Append(payload.OriginalLineCount)
                .Append(')')
                .AppendLine();
        }
    }

    private void UpdateUsageFromChunk(ChatStreamChunk chunk)
    {
        if (chunk.InputTokens.HasValue)
            _inputTokens = chunk.InputTokens;
        if (chunk.OutputTokens.HasValue)
            _outputTokens = chunk.OutputTokens;
        if (chunk.EstimatedCost.HasValue)
            _estimatedCost = chunk.EstimatedCost;
    }

    private string BuildHighlightedMarkup(string content, string? filePath, string? languageId = null)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        var language = languageId ?? LanguageDetector.FromPath(filePath);

        if (_syntaxHighlighter != null && !string.IsNullOrEmpty(language))
        {
            var builder = new StringBuilder(content.Length + 64);
            var renderer = new MarkupTokenRenderer(builder);
            _syntaxHighlighter.Highlight(content, language, renderer);
            return builder.ToString();
        }

        return ConsoleStyles.Escape(content);
    }
}
