using System.Text;
using DiffPlex;
using DiffPlex.Model;
using DiffPlex.Chunkers;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models.FileEdit;

namespace CodePunk.Core.Services;

/// <summary>
/// Service for generating unified diffs and computing statistics using DiffPlex
/// </summary>
public class DiffService : IDiffService
{
    private readonly IDiffer _differ;

    public DiffService()
    {
        _differ = new Differ();
    }

    /// <summary>
    /// Create unified diff format between old and new text
    /// </summary>
    public string CreateUnifiedDiff(string fileName, string oldText, string newText, int context = 3)
    {
        if (string.Equals(oldText, newText, StringComparison.Ordinal))
            return string.Empty;

        var oldLines = SplitLines(oldText ?? string.Empty);
        var newLines = SplitLines(newText ?? string.Empty);

        var diffResult = _differ.CreateDiffs(oldText ?? string.Empty, newText ?? string.Empty, false, false, new LineChunker());

        return FormatAsUnifiedDiff(fileName, diffResult, oldLines, newLines, context);
    }

    /// <summary>
    /// Compute statistics comparing original content to AI proposal and user final version
    /// </summary>
    public DiffStats ComputeStats(string original, string aiProposal, string userFinal)
    {
        var aiDiff = _differ.CreateDiffs(original ?? string.Empty, aiProposal ?? string.Empty, false, false, new LineChunker());
        var userDiff = _differ.CreateDiffs(aiProposal ?? string.Empty, userFinal ?? string.Empty, false, false, new LineChunker());

        var aiStats = CalculateStatsFromDiff(aiDiff);
        var userStats = CalculateStatsFromDiff(userDiff);

        return new DiffStats(
            LinesAdded: aiStats.LinesAdded + userStats.LinesAdded,
            LinesRemoved: aiStats.LinesRemoved + userStats.LinesRemoved,
            CharsAdded: aiStats.CharsAdded + userStats.CharsAdded,
            CharsRemoved: aiStats.CharsRemoved + userStats.CharsRemoved);
    }

    private static string[] SplitLines(string text)
    {
        return text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
    }

    private string FormatAsUnifiedDiff(string fileName, DiffResult diffResult, string[] oldLines, string[] newLines, int context)
    {
        if (diffResult.DiffBlocks == null || !diffResult.DiffBlocks.Any())
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine($"--- a/{fileName}");
        sb.AppendLine($"+++ b/{fileName}");

        var hunks = CreateHunks(diffResult, oldLines, newLines, context);

        foreach (var hunk in hunks)
        {
            sb.AppendLine($"@@ -{hunk.OldStart},{hunk.OldCount} +{hunk.NewStart},{hunk.NewCount} @@");
            foreach (var line in hunk.Lines)
            {
                sb.AppendLine(line);
            }
        }

        return sb.ToString();
    }

    private List<DiffHunk> CreateHunks(DiffResult diffResult, string[] oldLines, string[] newLines, int context)
    {
        var hunks = new List<DiffHunk>();

        foreach (var block in diffResult.DiffBlocks)
        {
            var hunkLines = new List<string>();

            // Add context before
            var contextStart = Math.Max(0, block.DeleteStartA - context);
            var actualStart = Math.Max(0, block.DeleteStartA);

            for (int i = contextStart; i < actualStart && i < oldLines.Length; i++)
            {
                hunkLines.Add($" {oldLines[i]}");
            }

            // Add deleted lines
            for (int i = 0; i < block.DeleteCountA; i++)
            {
                var lineIndex = block.DeleteStartA + i;
                if (lineIndex < oldLines.Length)
                {
                    hunkLines.Add($"-{oldLines[lineIndex]}");
                }
            }

            // Add inserted lines
            for (int i = 0; i < block.InsertCountB; i++)
            {
                var lineIndex = block.InsertStartB + i;
                if (lineIndex < newLines.Length)
                {
                    hunkLines.Add($"+{newLines[lineIndex]}");
                }
            }

            // Add context after
            var contextEnd = Math.Min(oldLines.Length, actualStart + block.DeleteCountA + context);
            for (int i = actualStart + block.DeleteCountA; i < contextEnd; i++)
            {
                hunkLines.Add($" {oldLines[i]}");
            }

            if (hunkLines.Count > 0)
            {
                var oldStart = contextStart + 1;
                var oldCount = (actualStart + block.DeleteCountA) - contextStart;
                var newStart = Math.Max(1, block.InsertStartB - (actualStart - contextStart) + 1);
                var newCount = oldCount - block.DeleteCountA + block.InsertCountB;

                hunks.Add(new DiffHunk(oldStart, oldCount, newStart, newCount, hunkLines));
            }
        }

        return hunks;
    }

    private (int LinesAdded, int LinesRemoved, int CharsAdded, int CharsRemoved) CalculateStatsFromDiff(DiffResult diffResult)
    {
        int linesAdded = 0, linesRemoved = 0, charsAdded = 0, charsRemoved = 0;

        if (diffResult.DiffBlocks != null)
        {
            foreach (var block in diffResult.DiffBlocks)
            {
                linesRemoved += block.DeleteCountA;
                linesAdded += block.InsertCountB;

                // Estimate character changes (rough approximation)
                charsRemoved += block.DeleteCountA * 50; // avg 50 chars per line
                charsAdded += block.InsertCountB * 50;
            }
        }

        return (linesAdded, linesRemoved, charsAdded, charsRemoved);
    }

    private record DiffHunk(int OldStart, int OldCount, int NewStart, int NewCount, List<string> Lines);
}