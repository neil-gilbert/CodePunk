using CodePunk.Core.Git;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodePunk.Core.GitSession;

public class GitSessionService : IGitSessionService
{
    private readonly IGitCommandExecutor _gitExecutor;
    private readonly IGitSessionStateStore _stateStore;
    private readonly GitSessionOptions _options;
    private readonly ILogger<GitSessionService> _logger;
    private GitSessionState? _currentSession;

    public GitSessionService(
        IGitCommandExecutor gitExecutor,
        IGitSessionStateStore stateStore,
        IOptions<GitSessionOptions> options,
        ILogger<GitSessionService> logger)
    {
        _gitExecutor = gitExecutor;
        _stateStore = stateStore;
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

        string? stashId = null;
        if (_options.StashUncommittedChanges)
        {
            var hasChangesResult = await _gitExecutor.HasUncommittedChangesAsync(cancellationToken);
            if (hasChangesResult.Success && hasChangesResult.Value)
            {
                var stashResult = await _gitExecutor.ExecuteAsync(
                    "stash push -u -m \"CodePunk: Auto-stash before AI session\"",
                    cancellationToken);

                if (stashResult.Success)
                {
                    stashId = "stash@{0}";
                    _logger.LogInformation("Stashed uncommitted changes");
                }
            }
        }

        var createBranchResult = await _gitExecutor.ExecuteAsync(
            $"checkout -b {shadowBranch}",
            cancellationToken);

        if (!createBranchResult.Success)
        {
            _logger.LogError("Failed to create shadow branch: {Error}", createBranchResult.Error);
            if (stashId != null)
            {
                await _gitExecutor.ExecuteAsync("stash pop", cancellationToken);
            }
            return null;
        }

        var session = GitSessionState.Create(shadowBranch, originalBranch, stashId);
        await _stateStore.SaveAsync(session, cancellationToken);

        _currentSession = session;
        _logger.LogInformation("Started git session {SessionId} on shadow branch {ShadowBranch}",
            session.SessionId, shadowBranch);

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

    public async Task<bool> AcceptSessionAsync(
        string commitMessage,
        CancellationToken cancellationToken = default)
    {
        if (_currentSession == null)
        {
            _logger.LogWarning("No active session to accept");
            return false;
        }

        try
        {
            _logger.LogInformation("Accepting session {SessionId}, checking out {OriginalBranch}",
                _currentSession.SessionId, _currentSession.OriginalBranch);

            var checkoutResult = await _gitExecutor.ExecuteAsync(
                $"checkout {_currentSession.OriginalBranch}",
                cancellationToken);

            if (!checkoutResult.Success)
            {
                _logger.LogError("Failed to checkout original branch {Branch}: {Error}",
                    _currentSession.OriginalBranch, checkoutResult.Error);
                return false;
            }

            _logger.LogInformation("Squash merging {ShadowBranch} into {OriginalBranch}",
                _currentSession.ShadowBranch, _currentSession.OriginalBranch);

            var mergeResult = await _gitExecutor.ExecuteAsync(
                $"merge --squash {_currentSession.ShadowBranch}",
                cancellationToken);

            if (!mergeResult.Success)
            {
                _logger.LogError("Failed to squash merge {ShadowBranch}: {Error}",
                    _currentSession.ShadowBranch, mergeResult.Error);
                return false;
            }

            var hasStagedChangesResult = await _gitExecutor.HasStagedChangesAsync(cancellationToken);
            _logger.LogInformation("Staged changes check: Success={Success}, Value={Value}",
                hasStagedChangesResult.Success, hasStagedChangesResult.Value);

            if (hasStagedChangesResult.Success && hasStagedChangesResult.Value)
            {
                _logger.LogInformation("Creating final commit with message: {Message}", commitMessage);

                var finalCommitResult = await _gitExecutor.ExecuteAsync(
                    $"commit -m \"{commitMessage.Replace("\"", "\\\"")}\"",
                    cancellationToken);

                if (!finalCommitResult.Success)
                {
                    _logger.LogError("Failed to create final commit: {Error}", finalCommitResult.Error);
                    return false;
                }
            }
            else if (!hasStagedChangesResult.Success)
            {
                _logger.LogWarning("Failed to check for staged changes: {Error}", hasStagedChangesResult.Error);
            }
            else
            {
                _logger.LogWarning("No staged changes after squash merge, skipping final commit");
            }

            _logger.LogInformation("Deleting shadow branch {ShadowBranch}", _currentSession.ShadowBranch);
            await _gitExecutor.ExecuteAsync($"branch -D {_currentSession.ShadowBranch}", cancellationToken);

            if (_currentSession.StashId != null)
            {
                _logger.LogInformation("Restoring stashed changes");
                var popResult = await _gitExecutor.ExecuteAsync("stash pop", cancellationToken);
                if (!popResult.Success)
                {
                    _logger.LogWarning("Failed to restore stash: {Error}", popResult.Error);
                }
            }

            _currentSession = _currentSession.MarkAccepted();
            await _stateStore.SaveAsync(_currentSession, cancellationToken);
            await _stateStore.DeleteAsync(_currentSession.SessionId, cancellationToken);

            _logger.LogInformation("Successfully accepted session {SessionId}", _currentSession.SessionId);
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
            _logger.LogInformation("Reverting session {SessionId}, reason: {Reason}", session.SessionId, reason);
            _logger.LogInformation("Checking out original branch: {OriginalBranch}", session.OriginalBranch);

            var checkoutResult = await _gitExecutor.ExecuteAsync(
                $"checkout {session.OriginalBranch}",
                cancellationToken);

            if (!checkoutResult.Success)
            {
                _logger.LogError("Failed to checkout original branch {Branch}: {Error}",
                    session.OriginalBranch, checkoutResult.Error);
            }

            if (_options.KeepFailedSessionBranches && session.IsFailed)
            {
                _logger.LogInformation("Keeping failed session branch {ShadowBranch} for debugging",
                    session.ShadowBranch);
            }
            else
            {
                _logger.LogInformation("Deleting shadow branch: {ShadowBranch}", session.ShadowBranch);
                var deleteBranchResult = await _gitExecutor.ExecuteAsync(
                    $"branch -D {session.ShadowBranch}",
                    cancellationToken);

                if (!deleteBranchResult.Success)
                {
                    _logger.LogWarning("Failed to delete shadow branch {ShadowBranch}: {Error}",
                        session.ShadowBranch, deleteBranchResult.Error);
                }
            }

            if (session.StashId != null)
            {
                _logger.LogInformation("Restoring stashed changes");
                var popResult = await _gitExecutor.ExecuteAsync("stash pop", cancellationToken);
                if (!popResult.Success)
                {
                    _logger.LogWarning("Failed to restore stash: {Error}", popResult.Error);
                }
            }

            await _stateStore.DeleteAsync(session.SessionId, cancellationToken);

            _logger.LogInformation("Successfully reverted session {SessionId}: {Reason}",
                session.SessionId, reason);

            if (_currentSession?.SessionId == session.SessionId)
            {
                _currentSession = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reverting session {SessionId}", session.SessionId);
        }
    }
}
