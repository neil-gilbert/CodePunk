using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodePunk.Core.Services;
using CodePunk.Core.Utils;

namespace CodePunk.Core.Tools;

public class GlobTool : ITool
{
    public string Name => "glob";

    public string Description =>
        "Finds files matching specific glob patterns (e.g., src/**/*.cs, *.md). " +
        "Returns absolute paths sorted by modification time (newest first). " +
        "Supports recursive search with ** patterns.";

    public JsonElement Parameters => JsonSchemaGenerator.Generate<GlobArgs>();

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!ToolArgumentBinder.TryBindAndValidate<GlobArgs>(arguments, out var args, out var error))
            {
                return new ToolResult { Content = error ?? "Invalid arguments", IsError = true, ErrorMessage = error };
            }

            var pattern = args!.Pattern!;
            var searchPath = string.IsNullOrWhiteSpace(args.Path) ? Directory.GetCurrentDirectory() : args.Path!;
            if (!Directory.Exists(searchPath))
            {
                return new ToolResult { Content = $"Directory not found: {searchPath}", IsError = true, ErrorMessage = $"Directory does not exist: {searchPath}" };
            }
            var caseSensitive = args.CaseSensitive ?? false;

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

public class GlobArgs
{
    [Required]
    [Display(Description = "The glob pattern to match (e.g., *.cs, src/**/*.txt)")]
    public string? Pattern { get; set; }

    [Display(Description = "Optional: Absolute path to the directory to search within. Defaults to current directory.")]
    public string? Path { get; set; }

    [Display(Description = "Optional: Whether the search should be case-sensitive. Defaults to false.")]
    public bool? CaseSensitive { get; set; }
}
