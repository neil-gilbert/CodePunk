using CodePunk.Core.Git;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodePunk.Core.GitSession;

public class GitSessionCleanupService : IHostedService
{
    private readonly IGitCommandExecutor _gitExecutor;
    private readonly IGitSessionStateStore _stateStore;
    private readonly GitSessionOptions _options;
    private readonly ILogger<GitSessionCleanupService> _logger;

    public GitSessionCleanupService(
        IGitCommandExecutor gitExecutor,
        IGitSessionStateStore stateStore,
        IOptions<GitSessionOptions> options,
        ILogger<GitSessionCleanupService> logger)
    {
        _gitExecutor = gitExecutor;
        _stateStore = stateStore;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !_options.CleanupOrphanedSessionsOnStartup)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Checking for orphaned git sessions...");

            var sessions = await _stateStore.LoadAllAsync(cancellationToken);
            var orphanedSessions = sessions.Where(ShouldAutoRevert).ToList();

            if (orphanedSessions.Count == 0)
            {
                _logger.LogInformation("No orphaned sessions found");
                return;
            }

            _logger.LogWarning("Found {Count} orphaned sessions, auto-reverting...", orphanedSessions.Count);

            foreach (var session in orphanedSessions)
            {
                await RevertSessionAsync(session, cancellationToken);
            }

            _logger.LogInformation("Cleanup complete: {Count} sessions reverted", orphanedSessions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during session cleanup");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private bool ShouldAutoRevert(GitSessionState session)
    {
        if (session.AcceptedAt.HasValue)
        {
            return false;
        }

        if (session.RejectedAt.HasValue || session.IsFailed)
        {
            return true;
        }

        var timeout = TimeSpan.FromMinutes(_options.SessionTimeoutMinutes);
        if (DateTimeOffset.UtcNow - session.LastActivityAt > timeout)
        {
            return true;
        }

        return true;
    }

    private async Task RevertSessionAsync(GitSessionState session, CancellationToken cancellationToken)
    {
        try
        {
            var currentBranchResult = await _gitExecutor.GetCurrentBranchAsync(cancellationToken);
            if (currentBranchResult.Success && currentBranchResult.Value == session.ShadowBranch)
            {
                await _gitExecutor.ExecuteAsync($"checkout {session.OriginalBranch}", cancellationToken);
            }

            if (_options.KeepFailedSessionBranches && session.IsFailed)
            {
                _logger.LogInformation("Keeping failed session branch {ShadowBranch}", session.ShadowBranch);
            }
            else
            {
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
                var hasStashResult = await _gitExecutor.ExecuteAsync("stash list", cancellationToken);
                if (hasStashResult.Success && hasStashResult.Output.Contains("CodePunk: Auto-stash"))
                {
                    await _gitExecutor.ExecuteAsync("stash pop", cancellationToken);
                }
            }

            await _stateStore.DeleteAsync(session.SessionId, cancellationToken);

            _logger.LogInformation("Reverted orphaned session {SessionId}", session.SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reverting session {SessionId}", session.SessionId);
        }
    }
}
