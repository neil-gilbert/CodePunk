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

/// <summary>
/// Tool for executing shell commands
/// </summary>
public class ShellTool : ITool
{
    public string Name => "shell";
    public string Description => "Execute a shell command and return the output";
    
    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            command = new
            {
                type = "string",
                description = "The shell command to execute"
            },
            workingDirectory = new
            {
                type = "string",
                description = "The working directory for the command (optional)"
            }
        },
        required = new[] { "command" }
    });

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!arguments.TryGetProperty("command", out var commandElement))
            {
                return new ToolResult
                {
                    Content = "Missing required parameter: command",
                    IsError = true,
                    ErrorMessage = "command parameter is required"
                };
            }

            var command = commandElement.GetString();
            if (string.IsNullOrEmpty(command))
            {
                return new ToolResult
                {
                    Content = "Invalid command",
                    IsError = true,
                    ErrorMessage = "Command cannot be empty"
                };
            }

            var workingDirectory = arguments.TryGetProperty("workingDirectory", out var wdElement) 
                ? wdElement.GetString() 
                : Directory.GetCurrentDirectory();

            using var process = new System.Diagnostics.Process();
            
            if (OperatingSystem.IsWindows())
            {
                process.StartInfo.FileName = "cmd.exe";
                process.StartInfo.Arguments = $"/c {command}";
            }
            else
            {
                process.StartInfo.FileName = "/bin/bash";
                process.StartInfo.Arguments = $"-c \"{command}\"";
            }

            process.StartInfo.WorkingDirectory = workingDirectory;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;

            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            
            await process.WaitForExitAsync(cancellationToken);
            
            var output = await outputTask;
            var error = await errorTask;

            var result = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(output))
            {
                result.AppendLine("STDOUT:");
                result.AppendLine(output);
            }
            
            if (!string.IsNullOrEmpty(error))
            {
                if (result.Length > 0) result.AppendLine();
                result.AppendLine("STDERR:");
                result.AppendLine(error);
            }

            var isError = process.ExitCode != 0;
            return new ToolResult
            {
                Content = result.Length > 0 ? result.ToString().Trim() : "Command completed with no output",
                IsError = isError,
                ErrorMessage = isError ? $"Command exited with code {process.ExitCode}" : null
            };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                Content = $"Error executing command: {ex.Message}",
                IsError = true,
                ErrorMessage = ex.Message
            };
        }
    }
}
