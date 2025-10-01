using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodePunk.Core.Services;

namespace CodePunk.Core.Tools;

public class GlobTool : ITool
{
    public string Name => "glob";

    public string Description =>
        "Finds files matching specific glob patterns (e.g., src/**/*.cs, *.md). " +
        "Returns absolute paths sorted by modification time (newest first). " +
        "Supports recursive search with ** patterns.";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            pattern = new
            {
                type = "string",
                description = "The glob pattern to match (e.g., *.cs, src/**/*.txt)"
            },
            path = new
            {
                type = "string",
                description = "Optional: The absolute path to the directory to search within. Defaults to current directory."
            },
            case_sensitive = new
            {
                type = "boolean",
                description = "Optional: Whether the search should be case-sensitive. Defaults to false."
            }
        },
        required = new[] { "pattern" }
    });

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!arguments.TryGetProperty("pattern", out var patternElement))
            {
                return new ToolResult
                {
                    Content = "Missing required parameter: pattern",
                    IsError = true,
                    ErrorMessage = "pattern parameter is required"
                };
            }

            var pattern = patternElement.GetString();
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return new ToolResult
                {
                    Content = "Invalid pattern",
                    IsError = true,
                    ErrorMessage = "Pattern cannot be empty"
                };
            }

            var searchPath = arguments.TryGetProperty("path", out var pathElement)
                ? pathElement.GetString()
                : Directory.GetCurrentDirectory();

            if (string.IsNullOrWhiteSpace(searchPath))
            {
                searchPath = Directory.GetCurrentDirectory();
            }

            if (!Directory.Exists(searchPath))
            {
                return new ToolResult
                {
                    Content = $"Directory not found: {searchPath}",
                    IsError = true,
                    ErrorMessage = $"Directory does not exist: {searchPath}"
                };
            }

            var caseSensitive = arguments.TryGetProperty("case_sensitive", out var caseElement)
                ? caseElement.GetBoolean()
                : false;

            var files = await Task.Run(() => FindMatchingFiles(searchPath, pattern, caseSensitive), cancellationToken);

            var sortedFiles = files
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => f.FullName)
                .ToList();

            var result = new StringBuilder();
            result.AppendLine($"Found {sortedFiles.Count} file(s) matching \"{pattern}\" within {searchPath}");
            result.AppendLine("Sorted by modification time (newest first):");
            result.AppendLine();

            foreach (var file in sortedFiles)
            {
                var relativePath = Path.GetRelativePath(searchPath, file);
                result.AppendLine(relativePath);
            }

            return new ToolResult { Content = result.ToString().Trim() };
        }
        catch (UnauthorizedAccessException ex)
        {
            return new ToolResult
            {
                Content = $"Access denied: {ex.Message}",
                IsError = true,
                ErrorMessage = "Permission denied"
            };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                Content = $"Error finding files: {ex.Message}",
                IsError = true,
                ErrorMessage = ex.Message
            };
        }
    }

    private static List<string> FindMatchingFiles(string searchPath, string pattern, bool caseSensitive)
    {
        var results = new List<string>();
        var isRecursive = pattern.Contains("**");

        if (isRecursive)
        {
            var parts = pattern.Split(new[] { "**" }, StringSplitOptions.None);
            var prefix = parts[0].TrimEnd('/', '\\');
            var suffix = parts.Length > 1 ? parts[1].TrimStart('/', '\\') : "*";

            var startPath = string.IsNullOrEmpty(prefix) ? searchPath : Path.Combine(searchPath, prefix);

            if (Directory.Exists(startPath))
            {
                var allFiles = Directory.EnumerateFiles(startPath, "*", SearchOption.AllDirectories);
                var regex = ConvertGlobToRegex(suffix, caseSensitive);

                foreach (var file in allFiles)
                {
                    var relativePath = Path.GetRelativePath(startPath, file);
                    if (regex.IsMatch(relativePath))
                    {
                        results.Add(file);
                    }
                }
            }
        }
        else
        {
            var directoryPattern = Path.GetDirectoryName(pattern) ?? "";
            var filePattern = Path.GetFileName(pattern);

            var targetDir = string.IsNullOrEmpty(directoryPattern)
                ? searchPath
                : Path.Combine(searchPath, directoryPattern);

            if (Directory.Exists(targetDir))
            {
                var regex = ConvertGlobToRegex(filePattern, caseSensitive);
                var files = Directory.EnumerateFiles(targetDir, "*", SearchOption.TopDirectoryOnly);

                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    if (regex.IsMatch(fileName))
                    {
                        results.Add(file);
                    }
                }
            }
        }

        return results;
    }

    private static Regex ConvertGlobToRegex(string pattern, bool caseSensitive)
    {
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
        return new Regex(regexPattern, options);
    }
}
