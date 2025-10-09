using System.IO;

namespace CodePunk.Core.SyntaxHighlighting;

/// <summary>
/// Provides helpers for inferring syntax highlighting language identifiers.
/// </summary>
public static class LanguageDetector
{
    /// <summary>
    /// Attempts to infer a language identifier from a filesystem path.
    /// </summary>
    public static string? FromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var extension = Path.GetExtension(path).ToLowerInvariant();

        return extension switch
        {
            ".cs" or ".csx" or ".razor" => "csharp",
            ".js" or ".jsx" or ".mjs" or ".cjs" => "javascript",
            ".ts" or ".tsx" or ".cts" or ".mts" => "typescript",
            ".py" or ".pyw" => "python",
            ".sql" => "sql",
            ".go" => "go",
            ".java" => "java",
            ".json" => "json",
            ".xml" or ".xaml" => "xml",
            ".html" or ".htm" => "html",
            ".css" => "css",
            ".md" => "markdown",
            _ => null
        };
    }
}
