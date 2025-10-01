using System.Text;
using System.Text.Json;
using CodePunk.Core.Services;

namespace CodePunk.Core.Tools;

public class ListDirectoryTool : ITool
{
    public string Name => "list_directory";

    public string Description =>
        "Lists files and subdirectories within a specified directory. " +
        "Returns file names, types (file/directory), sizes, and modification times. " +
        "Directories are listed first, then files, sorted alphabetically.";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            path = new
            {
                type = "string",
                description = "The absolute path to the directory to list"
            },
            ignore = new
            {
                type = "array",
                description = "Optional: Array of glob patterns to exclude from listing (e.g., *.log, .git)",
                items = new { type = "string" }
            }
        },
        required = new[] { "path" }
    });

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!arguments.TryGetProperty("path", out var pathElement))
            {
                return new ToolResult
                {
                    Content = "Missing required parameter: path",
                    IsError = true,
                    ErrorMessage = "path parameter is required"
                };
            }

            var dirPath = pathElement.GetString();
            if (string.IsNullOrWhiteSpace(dirPath))
            {
                return new ToolResult
                {
                    Content = "Invalid directory path",
                    IsError = true,
                    ErrorMessage = "Directory path cannot be empty"
                };
            }

            if (!Directory.Exists(dirPath))
            {
                return new ToolResult
                {
                    Content = $"Directory not found: {dirPath}",
                    IsError = true,
                    ErrorMessage = $"Directory does not exist: {dirPath}"
                };
            }

            var ignorePatterns = new List<string>();
            if (arguments.TryGetProperty("ignore", out var ignoreElement) && ignoreElement.ValueKind == JsonValueKind.Array)
            {
                ignorePatterns = ignoreElement.EnumerateArray()
                    .Select(e => e.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!)
                    .ToList();
            }

            var entries = Directory.GetFileSystemEntries(dirPath)
                .Select(entryPath =>
                {
                    var name = Path.GetFileName(entryPath);
                    if (ShouldIgnore(name, ignorePatterns))
                    {
                        return null;
                    }

                    var isDirectory = Directory.Exists(entryPath);
                    var size = 0L;
                    var modifiedTime = DateTime.MinValue;

                    try
                    {
                        var fileInfo = new FileInfo(entryPath);
                        if (fileInfo.Exists)
                        {
                            size = fileInfo.Length;
                            modifiedTime = fileInfo.LastWriteTime;
                        }
                        else
                        {
                            var dirInfo = new DirectoryInfo(entryPath);
                            modifiedTime = dirInfo.LastWriteTime;
                        }
                    }
                    catch
                    {
                    }

                    return new
                    {
                        Name = name,
                        Path = entryPath,
                        IsDirectory = isDirectory,
                        Size = size,
                        ModifiedTime = modifiedTime
                    };
                })
                .Where(e => e != null)
                .OrderByDescending(e => e!.IsDirectory)
                .ThenBy(e => e!.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var result = new StringBuilder();
            result.AppendLine($"Directory listing for {dirPath}:");
            result.AppendLine($"Total entries: {entries.Count}");
            result.AppendLine();

            foreach (var entry in entries)
            {
                if (entry!.IsDirectory)
                {
                    result.AppendLine($"[DIR]  {entry.Name}");
                }
                else
                {
                    var sizeStr = FormatFileSize(entry.Size);
                    var dateStr = entry.ModifiedTime.ToString("yyyy-MM-dd HH:mm:ss");
                    result.AppendLine($"[FILE] {entry.Name} ({sizeStr}, modified: {dateStr})");
                }
            }

            return new ToolResult { Content = result.ToString().Trim() };
        }
        catch (UnauthorizedAccessException ex)
        {
            return new ToolResult
            {
                Content = $"Access denied to directory: {ex.Message}",
                IsError = true,
                ErrorMessage = "Permission denied"
            };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                Content = $"Error listing directory: {ex.Message}",
                IsError = true,
                ErrorMessage = ex.Message
            };
        }
    }

    private static bool ShouldIgnore(string filename, List<string> patterns)
    {
        if (patterns.Count == 0)
        {
            return false;
        }

        foreach (var pattern in patterns)
        {
            var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";

            if (System.Text.RegularExpressions.Regex.IsMatch(filename, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }
}
