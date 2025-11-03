using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodePunk.Core.Services;
using CodePunk.Core.Utils;

namespace CodePunk.Core.Tools;

public class SearchFilesTool : ITool
{
    private const int MaxResultsPerFile = 100;
    private const int MaxTotalResults = 500;

    public string Name => "search_file_content";

    public string Description =>
        "Searches for a regular expression pattern within file contents in a specified directory. " +
        "Returns matching lines with file paths and line numbers. " +
        "Can filter files by glob pattern.";

    public JsonElement Parameters => JsonSchemaGenerator.Generate<SearchFilesArgs>();

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!ToolArgumentBinder.TryBindAndValidate<SearchFilesArgs>(arguments, out var args, out var error))
            {
                return new ToolResult { Content = error ?? "Invalid arguments", IsError = true, ErrorMessage = error };
            }

            var pattern = args!.Pattern!;
            var searchPath = string.IsNullOrWhiteSpace(args.Path) ? Directory.GetCurrentDirectory() : args.Path!;
            if (!Directory.Exists(searchPath))
            {
                return new ToolResult { Content = $"Directory not found: {searchPath}", IsError = true, ErrorMessage = $"Directory does not exist: {searchPath}" };
            }

            var includePattern = args.Include;
            var caseSensitive = args.CaseSensitive ?? false;

            Regex searchRegex;
            try
            {
                var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                searchRegex = new Regex(pattern, options);
            }
            catch (ArgumentException ex)
            {
                return new ToolResult
                {
                    Content = $"Invalid regex pattern: {ex.Message}",
                    IsError = true,
                    ErrorMessage = "Invalid regex pattern"
                };
            }

            var results = await Task.Run(() =>
                SearchFiles(searchPath, searchRegex, includePattern, cancellationToken), cancellationToken);

            return FormatResults(results, pattern, searchPath, includePattern);
        }
        catch (OperationCanceledException)
        {
            return new ToolResult
            {
                Content = "Search was cancelled",
                IsError = true,
                ErrorMessage = "Operation cancelled"
            };
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
                Content = $"Error searching files: {ex.Message}",
                IsError = true,
                ErrorMessage = ex.Message
            };
        }
    }

    private static List<SearchMatch> SearchFiles(
        string searchPath,
        Regex searchRegex,
        string? includePattern,
        CancellationToken cancellationToken)
    {
        var results = new List<SearchMatch>();
        var files = GetFilesToSearch(searchPath, includePattern);
        var totalResults = 0;

        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var lines = File.ReadAllLines(file);
                var fileResults = 0;

                for (var i = 0; i < lines.Length && fileResults < MaxResultsPerFile; i++)
                {
                    if (totalResults >= MaxTotalResults)
                    {
                        break;
                    }

                    var line = lines[i];
                    if (searchRegex.IsMatch(line))
                    {
                        results.Add(new SearchMatch
                        {
                            FilePath = file,
                            LineNumber = i + 1,
                            LineContent = line.Trim()
                        });
                        fileResults++;
                        totalResults++;
                    }
                }
            }
            catch
            {
            }
        }

        return results;
    }

    private static List<string> GetFilesToSearch(string searchPath, string? includePattern)
    {
        if (string.IsNullOrWhiteSpace(includePattern))
        {
            return Directory.EnumerateFiles(searchPath, "*", SearchOption.AllDirectories)
                .Where(f => !ShouldIgnoreFile(f))
                .ToList();
        }

        var isRecursive = includePattern.Contains("**");
        var files = new List<string>();

        if (isRecursive)
        {
            var parts = includePattern.Split(new[] { "**" }, StringSplitOptions.None);
            var prefix = parts[0].TrimEnd('/', '\\');
            var suffix = parts.Length > 1 ? parts[1].TrimStart('/', '\\') : "*";

            var startPath = string.IsNullOrEmpty(prefix) ? searchPath : Path.Combine(searchPath, prefix);

            if (Directory.Exists(startPath))
            {
                var allFiles = Directory.EnumerateFiles(startPath, "*", SearchOption.AllDirectories);
                var regex = ConvertGlobToRegex(suffix);

                foreach (var file in allFiles)
                {
                    if (ShouldIgnoreFile(file))
                    {
                        continue;
                    }

                    var relativePath = Path.GetRelativePath(startPath, file);
                    if (regex.IsMatch(relativePath))
                    {
                        files.Add(file);
                    }
                }
            }
        }
        else
        {
            var directoryPattern = Path.GetDirectoryName(includePattern) ?? "";
            var filePattern = Path.GetFileName(includePattern);

            var targetDir = string.IsNullOrEmpty(directoryPattern)
                ? searchPath
                : Path.Combine(searchPath, directoryPattern);

            if (Directory.Exists(targetDir))
            {
                var regex = ConvertGlobToRegex(filePattern);
                var allFiles = Directory.EnumerateFiles(targetDir, "*", SearchOption.TopDirectoryOnly);

                foreach (var file in allFiles)
                {
                    if (ShouldIgnoreFile(file))
                    {
                        continue;
                    }

                    var fileName = Path.GetFileName(file);
                    if (regex.IsMatch(fileName))
                    {
                        files.Add(file);
                    }
                }
            }
        }

        return files;
    }

    private static bool ShouldIgnoreFile(string filePath)
    {
        var ignoredDirs = new[] { "node_modules", ".git", "bin", "obj", ".vs", "packages" };
        var ignoredExtensions = new[] { ".dll", ".exe", ".bin", ".so", ".dylib" };

        var fileName = Path.GetFileName(filePath);
        var extension = Path.GetExtension(filePath);

        if (ignoredExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var ignoredDir in ignoredDirs)
        {
            if (filePath.Contains(Path.DirectorySeparatorChar + ignoredDir + Path.DirectorySeparatorChar) ||
                filePath.Contains(Path.DirectorySeparatorChar + ignoredDir + Path.DirectorySeparatorChar))
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

    private static ToolResult FormatResults(
        List<SearchMatch> results,
        string pattern,
        string searchPath,
        string? includePattern)
    {
        var content = new StringBuilder();
        var filterInfo = string.IsNullOrWhiteSpace(includePattern) ? "all files" : $"filter: \"{includePattern}\"";

        content.AppendLine($"Found {results.Count} match(es) for pattern \"{pattern}\" in path \"{searchPath}\" ({filterInfo}):");

        if (results.Count == 0)
        {
            content.AppendLine("No matches found.");
            return new ToolResult { Content = content.ToString().Trim() };
        }

        content.AppendLine();

        var groupedByFile = results.GroupBy(r => r.FilePath);

        foreach (var fileGroup in groupedByFile)
        {
            var relativePath = Path.GetRelativePath(searchPath, fileGroup.Key);
            content.AppendLine($"--- File: {relativePath} ---");

            foreach (var match in fileGroup)
            {
                content.AppendLine($"L{match.LineNumber}: {match.LineContent}");
            }

            content.AppendLine();
        }

        if (results.Count >= MaxTotalResults)
        {
            content.AppendLine($"Note: Results limited to {MaxTotalResults} matches. Refine your search for more specific results.");
        }

        return new ToolResult { Content = content.ToString().Trim() };
    }

    private class SearchMatch
    {
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string LineContent { get; set; } = string.Empty;
    }
}

public class SearchFilesArgs
{
    [Required]
    [Display(Description = "The regular expression pattern to search for (e.g., function\\s+myFunction)")]
    public string? Pattern { get; set; }

    [Display(Description = "Optional: Absolute path to the directory to search within. Defaults to current directory.")]
    public string? Path { get; set; }

    [Display(Description = "Optional: Glob pattern to filter which files are searched (e.g., *.cs, src/**/*.txt)")]
    public string? Include { get; set; }

    [Display(Description = "Optional: Whether the search should be case-sensitive. Defaults to false.")]
    public bool? CaseSensitive { get; set; }
}
