namespace CodePunk.Core.GitSession;

public interface IGitSessionService
{
    Task<GitSessionState?> GetCurrentSessionAsync(CancellationToken cancellationToken = default);

    Task<GitSessionState?> BeginSessionAsync(CancellationToken cancellationToken = default);

    Task<bool> CommitToolCallAsync(
        string toolName,
        string summary,
        CancellationToken cancellationToken = default);

    Task<bool> AcceptSessionAsync(
        string commitMessage,
        CancellationToken cancellationToken = default);

    Task<bool> RejectSessionAsync(CancellationToken cancellationToken = default);

    Task UpdateActivityAsync(CancellationToken cancellationToken = default);

    Task MarkAsFailedAsync(string reason, CancellationToken cancellationToken = default);

    bool IsEnabled { get; }
}
