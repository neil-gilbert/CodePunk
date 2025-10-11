using System;
using System.Diagnostics;
using System.Linq;
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
    private static readonly (string Pattern, string Recommendation)[] InteractiveCommandHints =
    {
        ("npm create", "Add --yes or use a predefined template to skip interactive prompts."),
        ("npm init", "Add --yes to accept defaults or initialize manually."),
        ("npx create-", "Pass --yes (or equivalent) to avoid interactive questions."),
        ("yarn create", "Supply --yes or specific flags to bypass prompts."),
        ("pnpm create", "Include --yes so the command does not wait for input."),
        ("create-react-app", "Use --template and --use-npm with --yes to skip questions."),
        ("dotnet new", "Include --force and disable restore (e.g. --no-restore) to make it non-interactive."),
        ("rails new", "Pass --skip-bundle or run the command manually."),
        ("mix phx.new", "Provide preset flags such as --install or --no-assets to avoid prompts."),
        ("cargo new", "Add --vcs none or run manually to control prompts."),
        ("flutter create", "Specify template flags (e.g. --project-name) to skip prompts.")
    };

    private static readonly string[] NonInteractiveTokens =
    {
        "--yes",
        "--no-interactive",
        "--skip-install",
        "--skip-bundle",
        "--skip-webpack-install",
        "--skip-deps",
        "--force",
        "--defaults",
        "--default",
        "--silence-warnings",
        "--quiet",
        "--no-input",
        "--no-restore",
        "--no-assets",
        "--non-interactive"
    };

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
            },
            stdin = new
            {
                type = "string",
                description = "Optional newline-delimited input that will be piped to the command's standard input."
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

            var stdin = arguments.TryGetProperty("stdin", out var stdinElement) && stdinElement.ValueKind == JsonValueKind.String
                ? stdinElement.GetString()
                : null;

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

            string? rewriteNote = null;
            if (stdin is null && TryRewriteInteractiveCommand(command, out var rewrittenCommand, out var rewrittenNote))
            {
                command = rewrittenCommand;
                rewriteNote = rewrittenNote;
            }
            else if (stdin is null && IsLikelyInteractiveCommand(command, out var recommendation))
            {
                var message = string.IsNullOrWhiteSpace(recommendation)
                    ? $"Command blocked: '{command}' is likely interactive and would hang in this environment. Add non-interactive flags or run it manually."
                    : $"Command blocked: '{command}' is likely interactive and would hang in this environment. {recommendation}";

                return new ToolResult
                {
                    Content = message,
                    IsError = true,
                    ErrorMessage = "INTERACTIVE_COMMAND_BLOCKED"
                };
            }

            var executionResult = await ExecuteCommandAsync(command, workingDirectory, stdin, cancellationToken);
            return FormatToolResult(executionResult, description, rewriteNote);
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
        string? stdinPayload,
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
            RedirectStandardInput = stdinPayload != null,
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

            if (stdinPayload != null)
            {
                await process.StandardInput.WriteAsync(stdinPayload);
                if (!stdinPayload.EndsWith('\n'))
                {
                    await process.StandardInput.WriteLineAsync();
                }
                await process.StandardInput.FlushAsync();
                process.StandardInput.Close();
            }

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

    private static bool IsLikelyInteractiveCommand(string command, out string recommendation)
    {
        var normalized = command.Trim();
        var lowered = normalized.ToLowerInvariant();

        if (NonInteractiveTokens.Any(token => lowered.Contains(token)))
        {
            recommendation = string.Empty;
            return false;
        }

        foreach (var (pattern, suggestion) in InteractiveCommandHints)
        {
            var loweredPattern = pattern.ToLowerInvariant();
            var index = lowered.IndexOf(loweredPattern, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            var preceding = index == 0 ? ' ' : lowered[index - 1];
            if (!char.IsWhiteSpace(preceding) && preceding != ';' && preceding != '&')
            {
                continue;
            }

            recommendation = suggestion;
            return true;
        }

        recommendation = string.Empty;
        return false;
    }

    private static bool TryRewriteInteractiveCommand(string command, out string rewritten, out string note)
    {
        var builder = new StringBuilder(command.Trim());
        var modified = false;
        note = string.Empty;

        if (ContainsPattern(command, "npx create-") || ContainsPattern(command, "npm create") ||
            ContainsPattern(command, "yarn create") || ContainsPattern(command, "pnpm create"))
        {
            modified |= EnsureFlag(builder, "--yes");
            if (modified)
            {
                note = "Added --yes so the create command runs without prompts.";
            }
        }
        else if (ContainsPattern(command, "dotnet new"))
        {
            var changed = EnsureFlag(builder, "--force");
            changed |= EnsureFlag(builder, "--no-restore");
            modified |= changed;
            if (changed)
            {
                note = "Added --force and --no-restore to keep dotnet new non-interactive.";
            }
        }

        rewritten = builder.ToString();
        return modified;
    }

    private static bool ContainsPattern(string command, string pattern) =>
        command.Contains(pattern, StringComparison.OrdinalIgnoreCase);

    private static bool EnsureFlag(StringBuilder builder, string flag)
    {
        if (builder.ToString().Contains(flag, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (builder.Length > 0 && !char.IsWhiteSpace(builder[^1]))
        {
            builder.Append(' ');
        }
        builder.Append(flag);
        return true;
    }

    private ToolResult FormatToolResult(ShellExecutionResult executionResult, string? description, string? note)
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

        if (!string.IsNullOrWhiteSpace(note))
        {
            contentBuilder.AppendLine($"Note: {note}");
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
