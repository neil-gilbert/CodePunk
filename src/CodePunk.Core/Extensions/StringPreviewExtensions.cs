using System.Text;

namespace CodePunk.Core.Extensions;

/// <summary>
/// Provides helpers for generating human-friendly previews of large text blocks.
/// </summary>
public static class StringPreviewExtensions
{
    public readonly record struct LinePreviewResult(
        string Preview,
        bool IsTruncated,
        int OriginalLineCount,
        int MaxLines);

    /// <summary>
    /// Returns structured information describing a line-limited preview of the supplied content.
    /// </summary>
    public static LinePreviewResult GetLinePreview(this string? content, int maxLines = 20)
    {
        if (string.IsNullOrEmpty(content) || maxLines <= 0)
            return new LinePreviewResult(string.Empty, false, 0, Math.Max(maxLines, 0));

        var normalized = content.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        if (lines.Length <= maxLines)
            return new LinePreviewResult(normalized, false, lines.Length, maxLines);

        var builder = new StringBuilder();
        for (var i = 0; i < maxLines; i++)
        {
            if (i > 0)
                builder.Append('\n');
            builder.Append(lines[i]);
        }

        return new LinePreviewResult(builder.ToString(), true, lines.Length, maxLines);
    }

    /// <summary>
    /// Returns a string limited to the first <paramref name="maxLines"/> lines, adding a footer when truncated.
    /// </summary>
    /// <param name="content">Original text content.</param>
    /// <param name="maxLines">Maximum number of lines to include.</param>
    /// <param name="footerBuilder">
    /// Optional footer factory invoked when the content is truncated.
    /// Receives the requested line limit and the original line count.
    /// </param>
    public static string ToLinePreview(
        this string? content,
        int maxLines = 20,
        Func<int, int, string>? footerBuilder = null)
    {
        var preview = content.GetLinePreview(maxLines);

        if (!preview.IsTruncated)
            return preview.Preview;

        var footer = footerBuilder?.Invoke(preview.MaxLines, preview.OriginalLineCount)
                     ?? $"... (showing first {preview.MaxLines} lines of {preview.OriginalLineCount})";

        var builder = new StringBuilder(preview.Preview.Length + footer.Length + 1);
        builder.Append(preview.Preview);
        builder.Append('\n');
        builder.Append(footer);

        return builder.ToString();
    }
}
