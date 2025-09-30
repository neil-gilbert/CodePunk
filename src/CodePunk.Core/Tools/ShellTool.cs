using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CodePunk.Core.Configuration;
using CodePunk.Core.Models.Shell;
using CodePunk.Core.Services;
using CodePunk.Core.Utils;
using Microsoft.Extensions.Options;

namespace CodePunk.Core.Tools;

public class ShellTool : ITool
{
    private readonly ShellCommandOptions _options;

    public ShellTool(IOptions<ShellCommandOptions> options)
    {
        _options = options.Value;
    }

    public string Name => "run_shell_command";

    public string Description =>
        "Execute a shell command and return the output. " +
        "On Windows, commands are executed with cmd.exe /c. " +
        "On other platforms, they are executed with bash -c. " +
        "The following information is returned: Command, Directory, Stdout, Stderr, Error, Exit Code.";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            command = new
            {
                type = "string",
                description = "The exact shell command to execute. " +
                             "WARNING: Command substitution using $(), ``, <(), or >() is not allowed for security reasons."
            },
            description = new
            {
                type = "string",
                description = "Brief description of the command for the user. " +
                             "Be specific and concise. Ideally a single sentence. " +
                             "Can be up to 3 sentences for clarity. No line breaks."
            },
            directory = new
            {
                type = "string",
                description = "The working directory for the command (optional). " +
                             "If not provided, the current directory is used."
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
            if (string.IsNullOrWhiteSpace(command))
            {
                return new ToolResult
                {
                    Content = "Invalid command: command cannot be empty",
                    IsError = true,
                    ErrorMessage = "Command cannot be empty"
                };
            }

            var description = arguments.TryGetProperty("description", out var descElement)
                ? descElement.GetString()
                : null;

            var workingDirectory = arguments.TryGetProperty("directory", out var dirElement)
                ? dirElement.GetString() ?? Directory.GetCurrentDirectory()
                : Directory.GetCurrentDirectory();

            if (_options.EnableCommandValidation)
            {
                var validationResult = ShellCommandValidator.ValidateCommand(
                    command,
                    _options.AllowedCommands.Count > 0 ? _options.AllowedCommands : null,
                    _options.BlockedCommands.Count > 0 ? _options.BlockedCommands : null);

                if (!validationResult.IsValid)
                {
                    return new ToolResult
                    {
                        Content = validationResult.ErrorMessage ?? "Command validation failed",
                        IsError = true,
                        ErrorMessage = validationResult.ErrorMessage
                    };
                }
            }

            var executionResult = await ExecuteCommandAsync(command, workingDirectory, cancellationToken);
            return FormatToolResult(executionResult, description);
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

    private async Task<ShellExecutionResult> ExecuteCommandAsync(
        string command,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var shellFileName = isWindows ? "cmd.exe" : "/bin/bash";
        var shellArguments = isWindows ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = shellFileName,
            Arguments = shellArguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.Environment["CODEPUNK_CLI"] = "1";
        startInfo.Environment["TERM"] = "xterm-256color";

        using var process = new Process { StartInfo = startInfo };
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                stdoutBuilder.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                stderrBuilder.AppendLine(e.Data);
            }
        };

        var wasCancelled = false;
        string? errorMessage = null;

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            wasCancelled = true;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }

        return new ShellExecutionResult
        {
            Command = command,
            Directory = workingDirectory,
            StandardOutput = stdoutBuilder.ToString().Trim(),
            StandardError = stderrBuilder.ToString().Trim(),
            ExitCode = process.HasExited ? process.ExitCode : null,
            ErrorMessage = errorMessage,
            WasCancelled = wasCancelled
        };
    }

    private ToolResult FormatToolResult(ShellExecutionResult executionResult, string? description)
    {
        var contentBuilder = new StringBuilder();

        contentBuilder.AppendLine($"Command: {executionResult.Command}");

        if (!string.IsNullOrWhiteSpace(description))
        {
            contentBuilder.AppendLine($"Description: {description}");
        }

        contentBuilder.AppendLine($"Directory: {executionResult.Directory}");

        if (!string.IsNullOrWhiteSpace(executionResult.StandardOutput))
        {
            contentBuilder.AppendLine($"Stdout: {executionResult.StandardOutput}");
        }
        else
        {
            contentBuilder.AppendLine("Stdout: (empty)");
        }

        if (!string.IsNullOrWhiteSpace(executionResult.StandardError))
        {
            contentBuilder.AppendLine($"Stderr: {executionResult.StandardError}");
        }
        else
        {
            contentBuilder.AppendLine("Stderr: (empty)");
        }

        if (executionResult.ErrorMessage != null)
        {
            contentBuilder.AppendLine($"Error: {executionResult.ErrorMessage}");
        }
        else
        {
            contentBuilder.AppendLine("Error: (none)");
        }

        if (executionResult.ExitCode.HasValue)
        {
            contentBuilder.AppendLine($"Exit Code: {executionResult.ExitCode.Value}");
        }
        else
        {
            contentBuilder.AppendLine("Exit Code: (none)");
        }

        if (executionResult.WasCancelled)
        {
            contentBuilder.AppendLine("Status: Cancelled by user");
        }

        var isError = executionResult.ExitCode.HasValue && executionResult.ExitCode.Value != 0;
        if (executionResult.ErrorMessage != null)
        {
            isError = true;
        }

        return new ToolResult
        {
            Content = contentBuilder.ToString().Trim(),
            IsError = isError,
            ErrorMessage = isError ? executionResult.ErrorMessage : null
        };
    }
}
