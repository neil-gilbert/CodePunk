using CodePunk.Core.Git;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodePunk.Core.GitSession;

public class GitSessionService : IGitSessionService
{
    private readonly IGitCommandExecutor _gitExecutor;
    private readonly IGitSessionStateStore _stateStore;
    private readonly IWorkingDirectoryProvider _workingDirProvider;
    private readonly GitSessionOptions _options;
    private readonly ILogger<GitSessionService> _logger;
    private GitSessionState? _currentSession;

    public GitSessionService(
        IGitCommandExecutor gitExecutor,
        IGitSessionStateStore stateStore,
        IWorkingDirectoryProvider workingDirProvider,
        IOptions<GitSessionOptions> options,
        ILogger<GitSessionService> logger)
    {
        _gitExecutor = gitExecutor;
        _stateStore = stateStore;
        _workingDirProvider = workingDirProvider;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsEnabled => _options.Enabled;

    public Task<GitSessionState?> GetCurrentSessionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_currentSession);
    }

    public async Task<GitSessionState?> BeginSessionAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Git session feature is disabled");
            return null;
        }

        var isRepoResult = await _gitExecutor.IsGitRepositoryAsync(cancellationToken);
        if (!isRepoResult.Success || !isRepoResult.Value)
        {
            _logger.LogInformation("Not a git repository, git session disabled");
            return null;
        }

        if (_currentSession != null && _currentSession.AcceptedAt == null && _currentSession.RejectedAt == null)
        {
            _logger.LogWarning("Auto-reverting previous session before starting new one");
            await RevertSessionInternalAsync(_currentSession, "New session started", cancellationToken);
        }

        var originalBranchResult = await _gitExecutor.GetCurrentBranchAsync(cancellationToken);
        if (!originalBranchResult.Success)
        {
            _logger.LogError("Failed to get current branch: {Error}", originalBranchResult.Error);
            return null;
        }

        var originalBranch = originalBranchResult.Value!;
        var sessionId = Guid.NewGuid().ToString("N");
        var shadowBranch = $"{_options.BranchPrefix}-{sessionId[..8]}";

        // Create worktree in temp directory
        var worktreeBasePath = _options.GetExpandedWorktreeBasePath();
        var worktreePath = Path.Combine(worktreeBasePath, sessionId);

        // Ensure base directory exists
        if (!Directory.Exists(worktreeBasePath))
        {
            Directory.CreateDirectory(worktreeBasePath);
        }

        _logger.LogInformation("Creating worktree at {WorktreePath} for session {SessionId}",
            worktreePath, sessionId);

        var createWorktreeResult = await _gitExecutor.ExecuteAsync(
            $"worktree add \"{worktreePath}\" -b {shadowBranch}",
            cancellationToken);

        if (!createWorktreeResult.Success)
        {
            _logger.LogError("Failed to create worktree: {Error}", createWorktreeResult.Error);
            return null;
        }

        // Update working directory provider to point to worktree
        _workingDirProvider.SetWorkingDirectory(worktreePath);

        // Create session state
        var session = GitSessionState.Create(shadowBranch, originalBranch, worktreePath);
        await _stateStore.SaveAsync(session, cancellationToken);

        _currentSession = session;
        _logger.LogInformation("Started git session {SessionId} in worktree {WorktreePath}",
            session.SessionId, worktreePath);

        return session;
    }

    public async Task<bool> CommitToolCallAsync(
        string toolName,
        string summary,
        CancellationToken cancellationToken = default)
    {
        if (_currentSession == null)
        {
            return false;
        }

        try
        {
            var stageResult = await _gitExecutor.ExecuteAsync("add -A", cancellationToken);
            if (!stageResult.Success)
            {
                _logger.LogWarning("Failed to stage changes: {Error}", stageResult.Error);
                return false;
            }

            var hasChangesResult = await _gitExecutor.HasUncommittedChangesAsync(cancellationToken);
            if (!hasChangesResult.Success || !hasChangesResult.Value)
            {
                _logger.LogInformation("No changes to commit for tool {ToolName}", toolName);
                return true;
            }

            var commitMessage = $"AI Tool: {toolName} - {summary}";
            var commitResult = await _gitExecutor.ExecuteAsync(
                $"commit -m \"{commitMessage.Replace("\"", "\\\"")}\"",
                cancellationToken);

            if (!commitResult.Success)
            {
                _logger.LogWarning("Failed to commit changes: {Error}", commitResult.Error);
                return false;
            }

            var commitHashResult = await _gitExecutor.ExecuteAsync("rev-parse HEAD", cancellationToken);
            if (!commitHashResult.Success)
            {
                _logger.LogWarning("Failed to get commit hash");
                return false;
            }

            var commitHash = commitHashResult.Output.Trim();
            var filesResult = await _gitExecutor.GetModifiedFilesAsync(cancellationToken);
            var files = filesResult.Success ? filesResult.Value! : new List<string>();

            var toolCommit = new GitToolCallCommit
            {
                ToolName = toolName,
                CommitHash = commitHash,
                CommittedAt = DateTimeOffset.UtcNow,
                FilesChanged = files
            };

            _currentSession = _currentSession.WithCommit(toolCommit);
            await _stateStore.SaveAsync(_currentSession, cancellationToken);

            _logger.LogInformation("Committed tool call {ToolName}: {CommitHash}", toolName, commitHash[..8]);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error committing tool call {ToolName}", toolName);
            return false;
        }
    }

    public async Task<bool> AcceptSessionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentSession == null)
        {
            _logger.LogWarning("No active session to accept");
            return false;
        }

        try
        {
            var worktreePath = _currentSession.WorktreePath;
            var originalDir = _workingDirProvider.GetOriginalDirectory();

            _logger.LogInformation("Accepting session {SessionId}, applying changes from worktree to {OriginalDir}",
                _currentSession.SessionId, originalDir);

            // Create a patch from the worktree (diff from original branch to current HEAD)
            var patchResult = await _gitExecutor.ExecuteAsync(
                $"diff {_currentSession.OriginalBranch} --binary",
                cancellationToken,
                workingDirectory: worktreePath);

            if (!patchResult.Success)
            {
                _logger.LogError("Failed to create patch: {Error}", patchResult.Error);
                return false;
            }

            // Apply patch to user's workspace (if there are changes)
            if (!string.IsNullOrWhiteSpace(patchResult.Output))
            {
                _logger.LogInformation("Applying patch to user workspace");

                // Write patch to temp file
                var patchFile = Path.Combine(Path.GetTempPath(), $"codepunk-patch-{_currentSession.SessionId}.patch");
                await File.WriteAllTextAsync(patchFile, patchResult.Output, cancellationToken);

                try
                {
                    var applyResult = await _gitExecutor.ExecuteAsync(
                        $"apply \"{patchFile}\"",
                        cancellationToken,
                        workingDirectory: originalDir);

                    if (!applyResult.Success)
                    {
                        _logger.LogError("Failed to apply patch to user workspace: {Error}", applyResult.Error);
                        return false;
                    }
                }
                finally
                {
                    if (File.Exists(patchFile))
                    {
                        File.Delete(patchFile);
                    }
                }
            }

            // Remove worktree
            _logger.LogInformation("Removing worktree {WorktreePath}", worktreePath);
            var removeWorktreeResult = await _gitExecutor.ExecuteAsync(
                $"worktree remove \"{worktreePath}\" --force",
                cancellationToken,
                workingDirectory: originalDir);

            if (!removeWorktreeResult.Success)
            {
                _logger.LogWarning("Failed to remove worktree (will cleanup manually): {Error}",
                    removeWorktreeResult.Error);

                // Manual cleanup
                if (Directory.Exists(worktreePath))
                {
                    Directory.Delete(worktreePath, recursive: true);
                }
            }

            // Delete shadow branch
            _logger.LogInformation("Deleting shadow branch {ShadowBranch}", _currentSession.ShadowBranch);
            await _gitExecutor.ExecuteAsync(
                $"branch -D {_currentSession.ShadowBranch}",
                cancellationToken,
                workingDirectory: originalDir);

            // Cleanup session state
            _currentSession = _currentSession.MarkAccepted();
            await _stateStore.SaveAsync(_currentSession, cancellationToken);
            await _stateStore.DeleteAsync(_currentSession.SessionId, cancellationToken);

            _logger.LogInformation("Successfully accepted session {SessionId}", _currentSession.SessionId);

            // Reset working directory provider
            _workingDirProvider.SetWorkingDirectory(originalDir);
            _currentSession = null;

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accepting session {SessionId}", _currentSession?.SessionId);
            return false;
        }
    }

    public async Task<bool> RejectSessionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentSession == null)
        {
            _logger.LogWarning("No active session to reject");
            return false;
        }

        _currentSession = _currentSession.MarkRejected();
        await RevertSessionInternalAsync(_currentSession, "User rejected", cancellationToken);
        return true;
    }

    public async Task UpdateActivityAsync(CancellationToken cancellationToken = default)
    {
        if (_currentSession != null)
        {
            _currentSession = _currentSession.UpdateActivity();
            await _stateStore.SaveAsync(_currentSession, cancellationToken);
        }
    }

    public async Task MarkAsFailedAsync(string reason, CancellationToken cancellationToken = default)
    {
        if (_currentSession != null)
        {
            _currentSession = _currentSession.MarkFailed(reason);
            await _stateStore.SaveAsync(_currentSession, cancellationToken);
            _logger.LogError("Session {SessionId} marked as failed: {Reason}",
                _currentSession.SessionId, reason);
        }
    }

    private async Task RevertSessionInternalAsync(
        GitSessionState session,
        string reason,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Reverting session {SessionId}, reason: {Reason}",
                session.SessionId, reason);

            var originalDir = _workingDirProvider.GetOriginalDirectory();
            var worktreePath = session.WorktreePath;

            // Remove worktree
            _logger.LogInformation("Removing worktree {WorktreePath}", worktreePath);
            var removeWorktreeResult = await _gitExecutor.ExecuteAsync(
                $"worktree remove \"{worktreePath}\" --force",
                cancellationToken,
                workingDirectory: originalDir);

            if (!removeWorktreeResult.Success)
            {
                _logger.LogWarning("Failed to remove worktree: {Error}", removeWorktreeResult.Error);

                // Manual cleanup
                if (Directory.Exists(worktreePath))
                {
                    Directory.Delete(worktreePath, recursive: true);
                }
            }

            // Delete shadow branch (unless keeping failed sessions)
            if (!(_options.KeepFailedSessionBranches && session.IsFailed))
            {
                _logger.LogInformation("Deleting shadow branch {ShadowBranch}", session.ShadowBranch);
                await _gitExecutor.ExecuteAsync(
                    $"branch -D {session.ShadowBranch}",
                    cancellationToken,
                    workingDirectory: originalDir);
            }
            else
            {
                _logger.LogInformation("Keeping failed session branch {ShadowBranch} for debugging",
                    session.ShadowBranch);
            }

            await _stateStore.DeleteAsync(session.SessionId, cancellationToken);

            _logger.LogInformation("Successfully reverted session {SessionId}: {Reason}",
                session.SessionId, reason);

            if (_currentSession?.SessionId == session.SessionId)
            {
                _workingDirProvider.SetWorkingDirectory(originalDir);
                _currentSession = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reverting session {SessionId}", session.SessionId);
        }
    }
}
