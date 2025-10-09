using CodePunk.Core.Abstractions;
using CodePunk.Core.Models.FileEdit;
using CodePunk.Core.SyntaxHighlighting.Abstractions;
using CodePunk.Core.SyntaxHighlighting.Tokenization;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Text.RegularExpressions;
using System.Text;

namespace CodePunk.Core.Services;

public class ConsoleApprovalService : IApprovalService
{
    private readonly ILogger<ConsoleApprovalService> _logger;
    private readonly ISyntaxHighlighter? _syntaxHighlighter;
    private bool _autoApproveSession = false;

    private const string AdditionBackground = "cadetblue";
    private const string DeletionBackground = "indianred";
    private const string ContextBackground = "grey11";

    public ConsoleApprovalService(
        ILogger<ConsoleApprovalService> logger,
        ISyntaxHighlighter? syntaxHighlighter = null)
    {
        _logger = logger;
        _syntaxHighlighter = syntaxHighlighter;
    }

    public async Task<ApprovalResult> RequestApprovalAsync(
        FileEditRequest request,
        string diff,
        DiffStats stats,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_autoApproveSession)
            {
                _logger.LogInformation("Auto-approving file edit (session auto-approval enabled): {FilePath}", request.FilePath);
                AnsiConsole.MarkupLine($"[green]✓[/] Auto-approving changes to [yellow]{request.FilePath}[/] (session auto-approval enabled)");
                return new ApprovalResult(true);
            }

            Console.WriteLine($"\nFile Edit Request: {request.FilePath}");
            Console.WriteLine($"Changes: +{stats.LinesAdded}/-{stats.LinesRemoved} lines, +{stats.CharsAdded}/-{stats.CharsRemoved} chars");

            if (!string.IsNullOrEmpty(diff))
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[dim]Changes Preview:[/]");

                var diffSections = ParseDiffIntoSections(diff);
                DisplaySideBySideDiff(diffSections, request, stats);
            }

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("\n[yellow]What would you like to do with these changes?[/]")
                    .AddChoices(new[] {
                        "Approve and apply changes",
                        "Approve all changes for this session",
                        "Cancel operation"
                    }));

            return choice switch
            {
                "Approve and apply changes" => HandleApproval(request.FilePath, true),
                "Approve all changes for this session" => HandleAutoApproval(request.FilePath),
                "Cancel operation" => HandleApproval(request.FilePath, false),
                _ => new ApprovalResult(false)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during approval request for {FilePath}", request.FilePath);
            return new ApprovalResult(false);
        }
    }

    private record DiffSection(int OldStartLine, int NewStartLine, List<DiffLine> Lines);
    private record DiffLine(DiffLineType Type, string Content, int OldLineNum, int NewLineNum);
    private enum DiffLineType { Context, Addition, Deletion }

    private static List<DiffSection> ParseDiffIntoSections(string diff)
    {
        if (string.IsNullOrEmpty(diff))
            return new List<DiffSection>();

        var lines = diff.Split('\n');
        var sections = new List<DiffSection>();
        var hunkHeaderRegex = new Regex(@"^@@ -(\d+),\d+ \+(\d+),\d+ @@");

        DiffSection? currentSection = null;
        int oldLineNum = 0;
        int newLineNum = 0;

        foreach (var line in lines)
        {
            if (line.StartsWith("@@"))
            {
                if (currentSection != null)
                    sections.Add(currentSection);

                var match = hunkHeaderRegex.Match(line);
                if (match.Success)
                {
                    oldLineNum = int.Parse(match.Groups[1].Value);
                    newLineNum = int.Parse(match.Groups[2].Value);
                    currentSection = new DiffSection(oldLineNum, newLineNum, new List<DiffLine>());
                }
            }
            else if (currentSection != null && !line.StartsWith("---") && !line.StartsWith("+++"))
            {
                DiffLineType type;
                string content = line.Length > 0 ? line[1..] : "";

                if (line.StartsWith("+"))
                {
                    type = DiffLineType.Addition;
                    currentSection.Lines.Add(new DiffLine(type, content, -1, newLineNum));
                    newLineNum++;
                }
                else if (line.StartsWith("-"))
                {
                    type = DiffLineType.Deletion;
                    currentSection.Lines.Add(new DiffLine(type, content, oldLineNum, -1));
                    oldLineNum++;
                }
                else if (line.StartsWith(" "))
                {
                    type = DiffLineType.Context;
                    currentSection.Lines.Add(new DiffLine(type, content, oldLineNum, newLineNum));
                    oldLineNum++;
                    newLineNum++;
                }
            }
        }

        if (currentSection != null)
            sections.Add(currentSection);

        return sections;
    }

    private void DisplaySideBySideDiff(List<DiffSection> sections, FileEditRequest request, DiffStats stats)
    {
        const int maxLinesTotal = 25;

        var mergedSections = MergeAdjacentSections(sections);
        var section = mergedSections.FirstOrDefault();

        if (section == null) return;

        var filteredLines = GetLinesWithContext(section.Lines, 2);

        if (!filteredLines.Any()) return;

        if (filteredLines.Count > maxLinesTotal)
        {
            var changeCount = filteredLines.Count(l => l.Type != DiffLineType.Context);
            AnsiConsole.MarkupLine($"[dim]Showing first {maxLinesTotal} lines ({changeCount} total changes)[/]");
            filteredLines = filteredLines.Take(maxLinesTotal).ToList();
        }

        AnsiConsole.MarkupLine($"[dim]Updated {request.FilePath} with {stats.LinesAdded} additions and {stats.LinesRemoved} removals[/]");

        var isFileCreation = filteredLines.All(l => l.Type == DiffLineType.Addition);
        var availableWidth = Math.Max(80, System.Console.WindowWidth - 10);

        string FormatLine(string numberMarkup, string indicatorMarkup, string bodyMarkup, string backgroundColor, int visibleLength)
        {
            var padding = Math.Max(0, availableWidth - visibleLength);
            return $"[on {backgroundColor}]{numberMarkup} {indicatorMarkup} {bodyMarkup}{new string(' ', padding)}[/]";
        }

        static string FormatLineNumber(int lineNumber, string color)
            => lineNumber >= 0 ? $"[{color}]{lineNumber,3}[/]" : $"[{color}]   [/]";

        if (isFileCreation)
        {
            var additionLines = filteredLines.Select(line =>
            {
                var highlighted = RenderHighlightedCode(line.Content, request.FilePath);
                var numberMarkup = FormatLineNumber(line.NewLineNum, AdditionBackground);
                return FormatLine(numberMarkup, "[white]+[/]", highlighted, AdditionBackground, 6 + line.Content.Length);
            });

            var creationPanel = new Panel(new Markup(string.Join("\n", additionLines)))
                .Header("[cadetblue]New File Content[/]", Justify.Left)
                .Border(BoxBorder.None)
                .Expand();

            var paddedCreationPanel = new Padder(creationPanel, new Padding(0, 0, 0, 7));
            AnsiConsole.Write(paddedCreationPanel);
            AnsiConsole.WriteLine();
            return;
        }

        var unifiedLines = new List<string>();

        foreach (var line in filteredLines)
        {
            switch (line.Type)
            {
                case DiffLineType.Context:
                    var lineNum = line.OldLineNum != -1 ? line.OldLineNum : line.NewLineNum;
                    var contextNumber = FormatLineNumber(lineNum, "dim");
                    var contextHighlighted = RenderHighlightedCode(line.Content, request.FilePath);
                    unifiedLines.Add(FormatLine(contextNumber, "[dim] [/]", contextHighlighted, ContextBackground, 6 + line.Content.Length));
                    break;
                case DiffLineType.Deletion:
                    var deleted = RenderHighlightedCode(line.Content, request.FilePath);
                    var deletionNumber = FormatLineNumber(line.OldLineNum, DeletionBackground);
                    unifiedLines.Add(FormatLine(deletionNumber, "[white]-[/]", deleted, DeletionBackground, 6 + line.Content.Length));
                    break;
                case DiffLineType.Addition:
                    var added = RenderHighlightedCode(line.Content, request.FilePath);
                    var additionNumber = FormatLineNumber(line.NewLineNum, AdditionBackground);
                    unifiedLines.Add(FormatLine(additionNumber, "[white]+[/]", added, AdditionBackground, 6 + line.Content.Length));
                    break;
                default:
                    continue;
            }
        }

        var diffPanel = new Panel(new Markup(string.Join("\n", unifiedLines)))
            .Header("[grey37]Changes[/]", Justify.Left)
            .Border(BoxBorder.None)
            .Expand();

        var paddedDiffPanel = new Padder(diffPanel, new Padding(0, 0, 0, 7));
        AnsiConsole.Write(paddedDiffPanel);
        AnsiConsole.WriteLine();
    }

    private static List<DiffSection> MergeAdjacentSections(List<DiffSection> sections)
    {
        if (sections.Count <= 1) return sections;

        var allLines = new List<DiffLine>();
        var seenLines = new HashSet<(int oldLine, int newLine, string content)>();

        int minOldStart = sections.Min(s => s.OldStartLine);
        int minNewStart = sections.Min(s => s.NewStartLine);

        foreach (var section in sections.OrderBy(s => s.OldStartLine))
        {
            foreach (var line in section.Lines)
            {
                var key = (line.OldLineNum, line.NewLineNum, line.Content);
                if (!seenLines.Contains(key))
                {
                    seenLines.Add(key);
                    allLines.Add(line);
                }
            }
        }

        allLines = allLines.OrderBy(l =>
        {
            if (l.Type == DiffLineType.Context || l.Type == DiffLineType.Deletion)
                return l.OldLineNum != -1 ? l.OldLineNum : int.MaxValue;
            else
                return l.NewLineNum != -1 ? l.NewLineNum : int.MaxValue;
        }).ToList();

        return new List<DiffSection>
        {
            new DiffSection(minOldStart, minNewStart, allLines)
        };
    }

    private static List<DiffLine> GetLinesWithContext(List<DiffLine> lines, int contextLines)
    {
        var result = new List<DiffLine>();
        var changedLineIndices = new HashSet<int>();

        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Type != DiffLineType.Context)
                changedLineIndices.Add(i);
        }

        var linesToInclude = new HashSet<int>();
        foreach (var changeIndex in changedLineIndices)
        {
            for (int i = Math.Max(0, changeIndex - contextLines);
                 i <= Math.Min(lines.Count - 1, changeIndex + contextLines);
                 i++)
            {
                linesToInclude.Add(i);
            }
        }

        foreach (var index in linesToInclude.OrderBy(x => x))
        {
            result.Add(lines[index]);
        }

        return result;
    }

    private string RenderHighlightedCode(string content, string filePath)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        var languageId = InferLanguageId(filePath);
        if (_syntaxHighlighter == null || string.IsNullOrEmpty(languageId))
            return Markup.Escape(content);

        var builder = new StringBuilder();
        var renderer = new MarkupBufferTokenRenderer(builder);
        _syntaxHighlighter.Highlight(content, languageId, renderer);
        return builder.ToString();
    }

    private static string? InferLanguageId(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        var extension = System.IO.Path.GetExtension(filePath).ToLowerInvariant();

        return extension switch
        {
            ".cs" or ".csx" or ".razor" => "csharp",
            _ => null
        };
    }

    private sealed class MarkupBufferTokenRenderer : ITokenRenderer
    {
        private static readonly IReadOnlyDictionary<TokenType, string> Colors = new Dictionary<TokenType, string>
        {
            [TokenType.Keyword] = "blue",
            [TokenType.Type] = "cyan",
            [TokenType.String] = "green",
            [TokenType.Comment] = "grey",
            [TokenType.Number] = "magenta",
            [TokenType.Operator] = "yellow",
            [TokenType.Punctuation] = "silver",
            [TokenType.Preprocessor] = "purple",
            [TokenType.Identifier] = "white",
            [TokenType.Text] = "default"
        };

        private readonly StringBuilder _builder;

        public MarkupBufferTokenRenderer(StringBuilder builder)
        {
            _builder = builder;
        }

        public void RenderToken(Token token)
        {
            var color = Colors.TryGetValue(token.Type, out var mapped) ? mapped : "default";
            var escaped = Markup.Escape(token.Value);

            if (color == "default")
            {
                _builder.Append(escaped);
            }
            else
            {
                _builder.Append('[').Append(color).Append(']').Append(escaped).Append("[/]");
            }
        }
    }

    private ApprovalResult HandleApproval(string filePath, bool approved)
    {
        var action = approved ? "approved" : "cancelled";
        _logger.LogInformation("User {Action} file edit: {FilePath}", action, filePath);
        return new ApprovalResult(approved);
    }

    private ApprovalResult HandleAutoApproval(string filePath)
    {
        _autoApproveSession = true;
        _logger.LogInformation("User enabled auto-approval for session and approved file edit: {FilePath}", filePath);
        AnsiConsole.MarkupLine("[green]✓[/] Enabled auto-approval for this session. All future changes will be automatically approved.");
        return new ApprovalResult(true);
    }

}
