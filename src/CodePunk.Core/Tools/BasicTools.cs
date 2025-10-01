using System.Text.Json;
using CodePunk.Core.Services;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models.FileEdit;

namespace CodePunk.Core.Tools;

/// <summary>
/// Tool for reading file contents with optional pagination support
/// </summary>
public class ReadFileTool : ITool
{
    private const int MaxLineLength = 2000;

    public string Name => "read_file";

    public string Description =>
        "Read the contents of a file from the filesystem. " +
        "Supports reading specific line ranges using offset and limit parameters. " +
        "Handles text files with pagination support for large files.";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            path = new
            {
                type = "string",
                description = "The absolute path to the file to read"
            },
            offset = new
            {
                type = "integer",
                description = "Optional: The 0-based line number to start reading from. Requires limit to be set."
            },
            limit = new
            {
                type = "integer",
                description = "Optional: Maximum number of lines to read. Use with offset to paginate through large files."
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

            var filePath = pathElement.GetString();
            if (string.IsNullOrEmpty(filePath))
            {
                return new ToolResult
                {
                    Content = "Invalid file path",
                    IsError = true,
                    ErrorMessage = "File path cannot be empty"
                };
            }

            if (!File.Exists(filePath))
            {
                return new ToolResult
                {
                    Content = $"File not found: {filePath}",
                    IsError = true,
                    ErrorMessage = $"File does not exist: {filePath}"
                };
            }

            var offset = arguments.TryGetProperty("offset", out var offsetElement)
                ? offsetElement.GetInt32()
                : (int?)null;

            var limit = arguments.TryGetProperty("limit", out var limitElement)
                ? limitElement.GetInt32()
                : (int?)null;

            if (offset.HasValue && offset.Value < 0)
            {
                return new ToolResult
                {
                    Content = "Offset must be a non-negative number",
                    IsError = true,
                    ErrorMessage = "Invalid offset value"
                };
            }

            if (limit.HasValue && limit.Value <= 0)
            {
                return new ToolResult
                {
                    Content = "Limit must be a positive number",
                    IsError = true,
                    ErrorMessage = "Invalid limit value"
                };
            }

            var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
            var totalLines = lines.Length;

            if (offset.HasValue || limit.HasValue)
            {
                var startLine = offset ?? 0;
                var linesToRead = limit ?? (totalLines - startLine);

                if (startLine >= totalLines)
                {
                    return new ToolResult
                    {
                        Content = $"Offset {startLine} exceeds total lines {totalLines} in file",
                        IsError = true,
                        ErrorMessage = "Offset out of range"
                    };
                }

                var endLine = Math.Min(startLine + linesToRead, totalLines);
                var selectedLines = lines.Skip(startLine).Take(endLine - startLine).ToArray();

                var truncatedLines = selectedLines
                    .Select(line => line.Length > MaxLineLength
                        ? line.Substring(0, MaxLineLength) + "... [line truncated]"
                        : line)
                    .ToArray();

                var content = string.Join("\n", truncatedLines);
                var nextOffset = endLine;
                var hasMore = endLine < totalLines;

                var message = $"IMPORTANT: The file content has been paginated.\n" +
                             $"Status: Showing lines {startLine + 1}-{endLine} of {totalLines} total lines.\n";

                if (hasMore)
                {
                    message += $"Action: To read more of the file, use offset: {nextOffset} in a subsequent read_file call.\n";
                }

                message += $"\n--- FILE CONTENT (lines {startLine + 1}-{endLine}) ---\n{content}";

                return new ToolResult { Content = message };
            }
            else
            {
                var truncatedLines = lines
                    .Select(line => line.Length > MaxLineLength
                        ? line.Substring(0, MaxLineLength) + "... [line truncated]"
                        : line)
                    .ToArray();

                var content = string.Join("\n", truncatedLines);
                return new ToolResult { Content = content };
            }
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                Content = $"Error reading file: {ex.Message}",
                IsError = true,
                ErrorMessage = ex.Message
            };
        }
    }
}

/// <summary>
/// Tool for writing complete file contents with diff generation and approval
/// </summary>
public class WriteFileTool : ITool
{
    private readonly IFileEditService _fileEditService;

    public WriteFileTool(IFileEditService fileEditService)
    {
        _fileEditService = fileEditService;
    }

    public string Name => "write_file";
    public string Description => "Write complete content to a file with diff generation and optional approval";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            file_path = new
            {
                type = "string",
                description = "The path to the file to write"
            },
            content = new
            {
                type = "string",
                description = "The complete content to write to the file"
            },
            require_approval = new
            {
                type = "boolean",
                description = "Whether to require user approval before writing (default: true)"
            }
        },
        required = new[] { "file_path", "content" }
    });

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!arguments.TryGetProperty("file_path", out var pathElement) ||
                !arguments.TryGetProperty("content", out var contentElement))
            {
                return new ToolResult
                {
                    Content = "Missing required parameters: file_path and content are required",
                    IsError = true,
                    ErrorMessage = "Both file_path and content parameters are required"
                };
            }

            var filePath = pathElement.GetString();
            var content = contentElement.GetString();
            var requireApproval = arguments.TryGetProperty("require_approval", out var approvalElement)
                ? approvalElement.GetBoolean()
                : true;

            if (string.IsNullOrEmpty(filePath))
            {
                return new ToolResult
                {
                    Content = "Invalid file path",
                    IsError = true,
                    ErrorMessage = "File path cannot be empty"
                };
            }

            var request = new WriteFileRequest(filePath, content ?? string.Empty, requireApproval);
            var result = await _fileEditService.WriteFileAsync(request, cancellationToken);

            if (!result.Success)
            {
                // Handle user cancellation as success, not error
                if (result.ErrorCode == "USER_CANCELLED")
                {
                    return new ToolResult
                    {
                        Content = "Operation cancelled by user.",
                        IsError = false,
                        UserCancelled = true
                    };
                }

                return new ToolResult
                {
                    Content = result.ErrorMessage ?? "File write failed",
                    IsError = true,
                    ErrorMessage = result.ErrorMessage
                };
            }

            var message = $"Successfully wrote to {filePath}";
            if (result.Stats != null)
            {
                message += $". Changes: +{result.Stats.LinesAdded}/-{result.Stats.LinesRemoved} lines";
            }
            if (result.TokensSaved.HasValue && result.TokensSaved > 0)
            {
                message += $". Tokens saved (est): {result.TokensSaved}";
            }

            return new ToolResult { Content = message };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                Content = $"Error writing file: {ex.Message}",
                IsError = true,
                ErrorMessage = ex.Message
            };
        }
    }
}
