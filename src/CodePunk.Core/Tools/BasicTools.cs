using System.Text.Json;
using CodePunk.Core.Services;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models.FileEdit;

namespace CodePunk.Core.Tools;

/// <summary>
/// Tool for reading file contents
/// </summary>
public class ReadFileTool : ITool
{
    public string Name => "read_file";
    public string Description => "Read the contents of a file from the filesystem";
    
    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            path = new
            {
                type = "string",
                description = "The path to the file to read"
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

            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            return new ToolResult { Content = content };
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
/// Tool for writing complete file contents with diff generation and approval following Gemini CLI pattern
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
