using System.Text;
using CodePunk.Console.SyntaxHighlighting;
using CodePunk.Console.Themes;
using CodePunk.Core.SyntaxHighlighting.Abstractions;
using Spectre.Console;

namespace CodePunk.Console.Rendering;

/// <summary>
/// Renders unified diffs with optional syntax highlighting.
/// </summary>
public class DiffRenderer
{
    private readonly ISyntaxHighlighter? _syntaxHighlighter;

    public DiffRenderer(ISyntaxHighlighter? syntaxHighlighter)
    {
        _syntaxHighlighter = syntaxHighlighter;
    }

    public Panel Render(string diff, string path)
    {
        var markup = BuildMarkup(diff, path);
        return new Panel(new Markup(markup))
            .Header(ConsoleStyles.PanelTitle(path))
            .RoundedBorder();
    }

    private string BuildMarkup(string diff, string path)
    {
        if (string.IsNullOrWhiteSpace(diff))
        {
            return ConsoleStyles.Dim("(no changes)");
        }

        var languageId = InferLanguageId(path);
        var builder = new StringBuilder();
        var lines = diff.Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (i > 0)
                builder.Append('\n');

            if (string.IsNullOrEmpty(line))
                continue;

            if (line.StartsWith("--- ") || line.StartsWith("+++ "))
            {
                builder.Append("[dim]").Append(ConsoleStyles.Escape(line)).Append("[/]");
                continue;
            }

            if (line.StartsWith("@@"))
            {
                builder.Append("[yellow]").Append(ConsoleStyles.Escape(line)).Append("[/]");
                continue;
            }

            if (line.StartsWith("diff ") || line.StartsWith("index "))
            {
                builder.Append("[dim]").Append(ConsoleStyles.Escape(line)).Append("[/]");
                continue;
            }

            if (line.StartsWith("\\ No newline"))
            {
                builder.Append("[dim italic]").Append(ConsoleStyles.Escape(line)).Append("[/]");
                continue;
            }

            var prefix = line[0];
            if (prefix is '+' or '-' or ' ')
            {
                var code = line.Length > 1 ? line[1..] : string.Empty;
                builder.Append(prefix switch
                {
                    '+' => WrapWithBackground($"[white]+[/] {HighlightCode(code, languageId)}", AdditionBackground),
                    '-' => WrapWithBackground($"[white]-[/] {HighlightCode(code, languageId)}", DeletionBackground),
                    _ => WrapWithBackground($"[dim] [/] {HighlightCode(code, languageId)}", ContextBackground)
                });
                continue;
            }

            builder.Append(ConsoleStyles.Escape(line));
        }

        return builder.ToString();
    }

    private static string WrapWithBackground(string content, string background)
        => $"[on {background}]{content}[/]";

    private string HighlightCode(string code, string? languageId)
    {
        if (string.IsNullOrEmpty(code))
            return string.Empty;

        if (_syntaxHighlighter == null || string.IsNullOrEmpty(languageId))
        {
            return ConsoleStyles.Escape(code);
        }

        var builder = new StringBuilder();
        var renderer = new MarkupTokenRenderer(builder);
        _syntaxHighlighter.Highlight(code, languageId, renderer);
        return builder.ToString();
    }

    private static string? InferLanguageId(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();

        return extension switch
        {
            ".cs" or ".csx" or ".razor" => "csharp",
            _ => null
        };
    }

    private const string AdditionBackground = "cadetblue";
    private const string DeletionBackground = "indianred";
    private const string ContextBackground = "grey11";
}
