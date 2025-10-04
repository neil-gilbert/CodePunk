using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace CodePunk.Core.Git;

public class GitCommandExecutor : IGitCommandExecutor
{
    private readonly ILogger<GitCommandExecutor> _logger;
    private readonly IWorkingDirectoryProvider _workingDirectoryProvider;
    private readonly string _gitExecutablePath;

    public GitCommandExecutor(
        ILogger<GitCommandExecutor> logger,
        IWorkingDirectoryProvider? workingDirectoryProvider = null)
    {
        _logger = logger;
        _workingDirectoryProvider = workingDirectoryProvider ?? new DefaultWorkingDirectoryProvider();
        _gitExecutablePath = FindGitExecutable();
    }

    private static string FindGitExecutable()
    {
        // Try to find git in PATH using 'which' (Unix) or 'where' (Windows)
        try
        {
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var command = isWindows ? "C:\\Windows\\System32\\where.exe" : "/usr/bin/which";
            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = "git",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = "/"  // Set to root to avoid any directory-dependent issues
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    // On Windows, 'where' returns all matches on separate lines; take the first
                    return output.Split('\n')[0].Trim();
                }
            }
        }
        catch
        {
            // Fall back to just "git" if we can't find the full path
        }

        return "git";
    }

    public async Task<GitOperationResult> ExecuteAsync(string arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            var workingDirectory = _workingDirectoryProvider.GetWorkingDirectory();

            var startInfo = new ProcessStartInfo
            {
                FileName = _gitExecutablePath,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    stdoutBuilder.AppendLine(e.Data);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    stderrBuilder.AppendLine(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            var output = stdoutBuilder.ToString().Trim();
            var error = stderrBuilder.ToString().Trim();
            var exitCode = process.ExitCode;

            if (exitCode != 0)
            {
                _logger.LogWarning("Git command failed: git {Arguments} (exit code {ExitCode})", arguments, exitCode);
                return GitOperationResult.Failed(string.IsNullOrEmpty(error) ? output : error, exitCode);
            }

            return GitOperationResult.Succeeded(output);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing git command: git {Arguments}", arguments);
            return GitOperationResult.Failed($"Failed to execute git command: {ex.Message}");
        }
    }

    public async Task<GitOperationResult<string>> GetCurrentBranchAsync(CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync("branch --show-current", cancellationToken);
        if (!result.Success)
        {
            return GitOperationResult<string>.Failed(result.Error, result.ExitCode);
        }

        var branchName = result.Output.Trim();
        if (string.IsNullOrEmpty(branchName))
        {
            return GitOperationResult<string>.Failed("Not on any branch (detached HEAD)");
        }

        return GitOperationResult<string>.Succeeded(branchName);
    }

    public async Task<GitOperationResult<bool>> IsGitRepositoryAsync(CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync("rev-parse --is-inside-work-tree", cancellationToken);
        var isRepo = result.Success && result.Output.Trim() == "true";
        return GitOperationResult<bool>.Succeeded(isRepo);
    }

    public async Task<GitOperationResult<List<string>>> GetModifiedFilesAsync(CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync("diff --name-only HEAD", cancellationToken);
        if (!result.Success)
        {
            return GitOperationResult<List<string>>.Failed(result.Error, result.ExitCode);
        }

        var files = result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .Where(f => !string.IsNullOrEmpty(f))
            .ToList();

        return GitOperationResult<List<string>>.Succeeded(files);
    }

    public async Task<GitOperationResult<bool>> HasUncommittedChangesAsync(CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync("status --porcelain", cancellationToken);
        if (!result.Success)
        {
            return GitOperationResult<bool>.Failed(result.Error, result.ExitCode);
        }

        var hasChanges = !string.IsNullOrWhiteSpace(result.Output);
        return GitOperationResult<bool>.Succeeded(hasChanges);
    }

    public async Task<GitOperationResult<bool>> HasStagedChangesAsync(CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync("diff --cached --quiet", cancellationToken);
        var hasStagedChanges = result.ExitCode != 0;
        return GitOperationResult<bool>.Succeeded(hasStagedChanges);
    }
}
