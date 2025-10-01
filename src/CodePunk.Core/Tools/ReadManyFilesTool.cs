using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodePunk.Core.Services;

namespace CodePunk.Core.Tools;

public class ReadManyFilesTool : ITool
{
    private const int MaxFiles = 50;
    private const int MaxLinesPerFile = 1000;

    public string Name => "read_many_files";

    public string Description =>
        "Reads and returns the content of multiple files matching specified patterns. " +
        "More efficient than calling read_file multiple times. " +
        "Supports glob patterns and filtering.";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            paths = new
            {
                type = "array",
                description = "Array of file paths or glob patterns to read (e.g., ['file1.txt', 'src/**/*.cs'])",
                items = new { type = "string" }
            },
            exclude = new
            {
                type = "array",
                description = "Optional: Array of glob patterns to exclude (e.g., ['*.log', 'test/**'])",
                items = new { type = "string" }
            }
        },
        required = new[] { "paths" }
    });

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!arguments.TryGetProperty("paths", out var pathsElement) ||
                pathsElement.ValueKind != JsonValueKind.Array)
            {
                return new ToolResult
                {
                    Content = "Missing required parameter: paths (must be an array)",
                    IsError = true,
                    ErrorMessage = "paths parameter is required and must be an array"
                };
            }

            var paths = pathsElement.EnumerateArray()
                .Select(e => e.GetString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .ToList();

            if (paths.Count == 0)
            {
                return new ToolResult
                {
                    Content = "No valid paths provided",
                    IsError = true,
                    ErrorMessage = "At least one path is required"
                };
            }

            var excludePatterns = new List<string>();
            if (arguments.TryGetProperty("exclude", out var excludeElement) &&
                excludeElement.ValueKind == JsonValueKind.Array)
            {
                excludePatterns = excludeElement.EnumerateArray()
                    .Select(e => e.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!)
                    .ToList();
            }

            var filesToRead = await Task.Run(() =>
                ResolveFiles(paths, excludePatterns), cancellationToken);

            if (filesToRead.Count == 0)
            {
                return new ToolResult
                {
                    Content = "No files matched the specified patterns",
                    IsError = false
                };
            }

            if (filesToRead.Count > MaxFiles)
            {
                return new ToolResult
                {
                    Content = $"Too many files matched ({filesToRead.Count}). Maximum is {MaxFiles}. " +
                             "Please refine your patterns to match fewer files.",
                    IsError = true,
                    ErrorMessage = "Too many files"
                };
            }

            var results = await ReadFilesAsync(filesToRead, cancellationToken);
            return FormatResults(results);
        }
        catch (OperationCanceledException)
        {
            return new ToolResult
            {
                Content = "Operation was cancelled",
                IsError = true,
                ErrorMessage = "Operation cancelled"
            };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                Content = $"Error reading files: {ex.Message}",
                IsError = true,
                ErrorMessage = ex.Message
            };
        }
    }

    private static List<string> ResolveFiles(List<string> patterns, List<string> excludePatterns)
    {
        var files = new HashSet<string>();
        var currentDir = Directory.GetCurrentDirectory();

        foreach (var pattern in patterns)
        {
            var isGlobPattern = pattern.Contains('*') || pattern.Contains('?');

            if (isGlobPattern)
            {
                var matchedFiles = FindFilesMatchingGlob(currentDir, pattern);
                foreach (var file in matchedFiles)
                {
                    if (!ShouldExclude(file, excludePatterns))
                    {
                        files.Add(Path.GetFullPath(file));
                    }
                }
            }
            else if (File.Exists(pattern))
            {
                if (!ShouldExclude(pattern, excludePatterns))
                {
                    files.Add(Path.GetFullPath(pattern));
                }
            }
            else if (Directory.Exists(pattern))
            {
                var dirFiles = Directory.EnumerateFiles(pattern, "*", SearchOption.TopDirectoryOnly);
                foreach (var file in dirFiles)
                {
                    if (!ShouldExclude(file, excludePatterns))
                    {
                        files.Add(Path.GetFullPath(file));
                    }
                }
            }
            else
            {
                if (!ShouldExclude(pattern, excludePatterns))
                {
                    files.Add(Path.GetFullPath(pattern));
                }
            }
        }

        return files.OrderBy(f => f).ToList();
    }

    private static List<string> FindFilesMatchingGlob(string basePath, string pattern)
    {
        var results = new List<string>();
        var isRecursive = pattern.Contains("**");

        if (isRecursive)
        {
            var parts = pattern.Split(new[] { "**" }, StringSplitOptions.None);
            var prefix = parts[0].TrimEnd('/', '\\');
            var suffix = parts.Length > 1 ? parts[1].TrimStart('/', '\\') : "*";

            var startPath = string.IsNullOrEmpty(prefix)
                ? basePath
                : (Path.IsPathRooted(prefix) ? prefix : Path.Combine(basePath, prefix));

            if (Directory.Exists(startPath))
            {
                var allFiles = Directory.EnumerateFiles(startPath, "*", SearchOption.AllDirectories);
                var regex = ConvertGlobToRegex(suffix);

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
                ? basePath
                : (Path.IsPathRooted(directoryPattern) ? directoryPattern : Path.Combine(basePath, directoryPattern));

            if (Directory.Exists(targetDir))
            {
                var regex = ConvertGlobToRegex(filePattern);
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

    private static bool ShouldExclude(string filePath, List<string> excludePatterns)
    {
        if (excludePatterns.Count == 0)
        {
            return false;
        }

        var fileName = Path.GetFileName(filePath);

        foreach (var pattern in excludePatterns)
        {
            var regex = ConvertGlobToRegex(pattern);
            if (regex.IsMatch(fileName) || regex.IsMatch(filePath))
            {
                return true;
            }
        }

        return false;
    }

    private static Regex ConvertGlobToRegex(string pattern)
    {
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return new Regex(regexPattern, RegexOptions.IgnoreCase);
    }

    private static async Task<List<FileReadResult>> ReadFilesAsync(
        List<string> files,
        CancellationToken cancellationToken)
    {
        var results = new List<FileReadResult>();

        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var lines = await File.ReadAllLinesAsync(file, cancellationToken);
                var truncated = false;

                if (lines.Length > MaxLinesPerFile)
                {
                    lines = lines.Take(MaxLinesPerFile).ToArray();
                    truncated = true;
                }

                var content = string.Join("\n", lines);

                results.Add(new FileReadResult
                {
                    FilePath = file,
                    Content = content,
                    Success = true,
                    LineCount = lines.Length,
                    Truncated = truncated
                });
            }
            catch (Exception ex)
            {
                results.Add(new FileReadResult
                {
                    FilePath = file,
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        return results;
    }

    private static ToolResult FormatResults(List<FileReadResult> results)
    {
        var content = new StringBuilder();
        var successCount = results.Count(r => r.Success);
        var failureCount = results.Count - successCount;

        content.AppendLine($"Read {successCount} file(s) successfully");
        if (failureCount > 0)
        {
            content.AppendLine($"Failed to read {failureCount} file(s)");
        }
        content.AppendLine();

        foreach (var result in results)
        {
            var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), result.FilePath);
            content.AppendLine($"--- {relativePath} ---");

            if (result.Success)
            {
                if (result.Truncated)
                {
                    content.AppendLine($"[File truncated: showing first {MaxLinesPerFile} lines]");
                }

                content.AppendLine(result.Content);
            }
            else
            {
                content.AppendLine($"[Error reading file: {result.ErrorMessage}]");
            }

            content.AppendLine();
            content.AppendLine("--- End of content ---");
            content.AppendLine();
        }

        return new ToolResult { Content = content.ToString().Trim() };
    }

    private class FileReadResult
    {
        public string FilePath { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int LineCount { get; set; }
        public bool Truncated { get; set; }
    }
}
