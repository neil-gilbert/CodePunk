using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CodePunk.Tui.Rendering;

/// <summary>
/// Builds stable, display-ready lines for the TUI diff viewer.
/// Each returned string is a single logical row: "NNN +/- content" with no trailing newlines.
/// </summary>
public static class DiffLineFormatter
{
    private enum DiffLineType { Context, Addition, Deletion }
    private sealed record DiffLine(DiffLineType Type, string Content, int OldLineNum, int NewLineNum);
    private sealed record DiffSection(int OldStartLine, int NewStartLine, List<DiffLine> Lines);

    public static IReadOnlyList<string> BuildDisplayLines(string diff, int context = 2, int maxLines = 25)
    {
        if (string.IsNullOrWhiteSpace(diff)) return Array.Empty<string>();

        var sections = MergeAdjacentSections(ParseDiffIntoSections(diff));
        var section = sections.FirstOrDefault();
        if (section == null) return Array.Empty<string>();

        var filtered = GetLinesWithContext(section.Lines, context);
        if (filtered.Count == 0) return Array.Empty<string>();

        if (filtered.Count > maxLines) filtered = filtered.Take(maxLines).ToList();

        var result = new List<string>(filtered.Count);
        foreach (var line in filtered)
        {
            var indicator = line.Type == DiffLineType.Addition ? '+' : line.Type == DiffLineType.Deletion ? '-' : ' ';
            var number = FormatLineNumberText(line.Type == DiffLineType.Addition
                ? line.NewLineNum
                : line.OldLineNum != -1 ? line.OldLineNum : line.NewLineNum);
            var content = line.Content ?? string.Empty;
            result.Add(string.Concat(number, " ", indicator, " ", content));
        }
        return result;
    }

    private static string FormatLineNumberText(int lineNumber)
        => lineNumber >= 0 ? lineNumber.ToString().PadLeft(3) : "   ";

    private static List<DiffSection> ParseDiffIntoSections(string diff)
    {
        var lines = diff.Split('\n');
        var sections = new List<DiffSection>();
        var hunk = new Regex(@"^@@ -(\d+),\d+ \+(\d+),\d+ @@");
        DiffSection? current = null;
        int oldLine = 0, newLine = 0;

        foreach (var line in lines)
        {
            if (line.StartsWith("@@"))
            {
                if (current != null) sections.Add(current);
                var m = hunk.Match(line);
                if (m.Success)
                {
                    oldLine = int.Parse(m.Groups[1].Value);
                    newLine = int.Parse(m.Groups[2].Value);
                    current = new DiffSection(oldLine, newLine, new List<DiffLine>());
                }
            }
            else if (current != null && !line.StartsWith("---") && !line.StartsWith("+++"))
            {
                if (line.StartsWith("+"))
                {
                    var body = line.Length > 1 ? line[1..] : string.Empty;
                    current.Lines.Add(new DiffLine(DiffLineType.Addition, body, -1, newLine));
                    newLine++;
                }
                else if (line.StartsWith("-"))
                {
                    var body = line.Length > 1 ? line[1..] : string.Empty;
                    current.Lines.Add(new DiffLine(DiffLineType.Deletion, body, oldLine, -1));
                    oldLine++;
                }
                else
                {
                    var body = line.StartsWith(" ") ? (line.Length > 1 ? line[1..] : string.Empty) : line;
                    current.Lines.Add(new DiffLine(DiffLineType.Context, body, oldLine, newLine));
                    oldLine++; newLine++;
                }
            }
        }

        if (current != null) sections.Add(current);
        return sections;
    }

    private static List<DiffSection> MergeAdjacentSections(List<DiffSection> sections)
    {
        if (sections.Count <= 1) return sections;
        var allLines = new List<DiffLine>();
        var seen = new HashSet<(int oldLine, int newLine, string content, DiffLineType type)>();
        int minOld = sections.Min(s => s.OldStartLine);
        int minNew = sections.Min(s => s.NewStartLine);

        foreach (var s in sections.OrderBy(s => s.OldStartLine))
        {
            foreach (var l in s.Lines)
            {
                var key = (l.OldLineNum, l.NewLineNum, l.Content, l.Type);
                if (seen.Add(key)) allLines.Add(l);
            }
        }

        allLines = allLines.OrderBy(l =>
        {
            if (l.Type == DiffLineType.Context || l.Type == DiffLineType.Deletion)
                return l.OldLineNum != -1 ? l.OldLineNum : int.MaxValue;
            return l.NewLineNum != -1 ? l.NewLineNum : int.MaxValue;
        }).ToList();

        return new List<DiffSection> { new DiffSection(minOld, minNew, allLines) };
    }

    private static List<DiffLine> GetLinesWithContext(List<DiffLine> lines, int context)
    {
        var result = new List<DiffLine>();
        var changed = new HashSet<int>();
        for (int i = 0; i < lines.Count; i++) if (lines[i].Type != DiffLineType.Context) changed.Add(i);
        var include = new HashSet<int>();
        foreach (var idx in changed)
        {
            for (int i = Math.Max(0, idx - context); i <= Math.Min(lines.Count - 1, idx + context); i++) include.Add(i);
        }
        foreach (var i in include.OrderBy(x => x)) result.Add(lines[i]);
        return result;
    }
}

