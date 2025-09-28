using Microsoft.Extensions.Logging;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models.FileEdit;
using Spectre.Console;
using System.Text.RegularExpressions;

namespace CodePunk.Core.Services;

/// <summary>
/// Console-based approval service for file edit operations
/// </summary>
public class ConsoleApprovalService : IApprovalService
{
    private readonly ILogger<ConsoleApprovalService> _logger;
    private bool _autoApproveSession = false;

    public ConsoleApprovalService(ILogger<ConsoleApprovalService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Request user approval via console interface with diff preview
    /// </summary>
    public async Task<ApprovalResult> RequestApprovalAsync(
        FileEditRequest request,
        string diff,
        DiffStats stats,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if auto-approval is enabled for this session
            if (_autoApproveSession)
            {
                _logger.LogInformation("Auto-approving file edit (session auto-approval enabled): {FilePath}", request.FilePath);
                AnsiConsole.MarkupLine($"[green]✓[/] Auto-approving changes to [yellow]{request.FilePath}[/] (session auto-approval enabled)");
                return new ApprovalResult(true);
            }

            // Display file edit summary
            Console.WriteLine($"\nFile Edit Request: {request.FilePath}");
            Console.WriteLine($"Changes: +{stats.LinesAdded}/-{stats.LinesRemoved} lines, +{stats.CharsAdded}/-{stats.CharsRemoved} chars");

            // Show diff with size limits for readability
            if (!string.IsNullOrEmpty(diff))
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[yellow]Diff Preview:[/]");
                AnsiConsole.WriteLine(new string('─', 80));

                if (diff.Length > 2000)
                {
                    // Show truncated diff for large changes
                    var lines = diff.Split('\n');
                    var previewLines = lines.Take(50).ToArray();
                    var truncatedDiff = string.Join("\n", previewLines);

                    AnsiConsole.Write(FormatDiffWithColors(truncatedDiff));
                    AnsiConsole.MarkupLine($"\n[dim]... ({lines.Length - 50} more lines)[/]");
                }
                else
                {
                    AnsiConsole.Write(FormatDiffWithColors(diff));
                }

                AnsiConsole.WriteLine(new string('─', 80));
            }

            // Prompt for approval using Spectre selection
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

    private static Markup FormatDiffWithColors(string diff)
    {
        if (string.IsNullOrEmpty(diff))
            return new Markup(string.Empty);

        var lines = diff.Split('\n');
        var formattedLines = new List<string>();

        int oldLineNum = 0;
        int newLineNum = 0;
        var hunkHeaderRegex = new Regex(@"^@@ -(\d+),\d+ \+(\d+),\d+ @@");

        foreach (var line in lines)
        {
            if (line.StartsWith("@@"))
            {
                // Parse hunk header to get starting line numbers
                var match = hunkHeaderRegex.Match(line);
                if (match.Success)
                {
                    oldLineNum = int.Parse(match.Groups[1].Value);
                    newLineNum = int.Parse(match.Groups[2].Value);
                }
                // Skip displaying hunk headers - they're just technical metadata
                continue;
            }
            else if (line.StartsWith("+"))
            {
                // Addition: show new line number
                formattedLines.Add($"[white on green4]{Markup.Escape($"{newLineNum,4} {line}")}[/]");
                newLineNum++;
            }
            else if (line.StartsWith("-"))
            {
                // Deletion: show old line number
                formattedLines.Add($"[white on red3_1]{Markup.Escape($"{oldLineNum,4} {line}")}[/]");
                oldLineNum++;
            }
            else if (line.StartsWith(" "))
            {
                // Context: show both line numbers (using old line number) with no background
                formattedLines.Add(Markup.Escape($"{oldLineNum,4} {line}"));
                oldLineNum++;
                newLineNum++;
            }
            else if (line.StartsWith("---") || line.StartsWith("+++"))
            {
                // File headers
                formattedLines.Add($"[black on yellow3]{Markup.Escape(line)}[/]");
            }
            else
            {
                // Other lines (shouldn't normally happen in unified diff)
                formattedLines.Add(Markup.Escape(line));
            }
        }

        return new Markup(string.Join("\n", formattedLines));
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