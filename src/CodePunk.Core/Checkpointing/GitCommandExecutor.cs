using System.Diagnostics;
using System.Text;

namespace CodePunk.Core.Checkpointing;

public class GitCommandExecutor
{
    public async Task<GitCommandResult> ExecuteAsync(
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (sender, args) =>
        {
            if (args.Data != null) outputBuilder.AppendLine(args.Data);
        };

        process.ErrorDataReceived += (sender, args) =>
        {
            if (args.Data != null) errorBuilder.AppendLine(args.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        var output = outputBuilder.ToString();
        var error = errorBuilder.ToString();

        return new GitCommandResult(
            ExitCode: process.ExitCode,
            Output: output,
            Error: error,
            Success: process.ExitCode == 0);
    }
}

public record GitCommandResult(
    int ExitCode,
    string Output,
    string Error,
    bool Success);
