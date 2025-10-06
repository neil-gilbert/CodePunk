namespace CodePunk.Core.GitSession;

public record GitSessionState
{
    public required string SessionId { get; init; }
    public required string ShadowBranch { get; init; }
    public required string OriginalBranch { get; init; }
    public required string WorktreePath { get; init; }
    public List<GitToolCallCommit> ToolCallCommits { get; init; } = new();
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? AcceptedAt { get; init; }
    public DateTimeOffset? RejectedAt { get; init; }
    public DateTimeOffset LastActivityAt { get; init; }
    public bool IsFailed { get; init; }
    public string? FailureReason { get; init; }

    public static GitSessionState Create(string shadowBranch, string originalBranch, string worktreePath)
    {
        var now = DateTimeOffset.UtcNow;
        return new GitSessionState
        {
            SessionId = Guid.NewGuid().ToString("N"),
            ShadowBranch = shadowBranch,
            OriginalBranch = originalBranch,
            WorktreePath = worktreePath,
            StartedAt = now,
            LastActivityAt = now
        };
    }

    public GitSessionState WithCommit(GitToolCallCommit commit)
    {
        var commits = new List<GitToolCallCommit>(ToolCallCommits) { commit };
        return this with
        {
            ToolCallCommits = commits,
            LastActivityAt = DateTimeOffset.UtcNow
        };
    }

    public GitSessionState MarkAccepted()
    {
        return this with { AcceptedAt = DateTimeOffset.UtcNow };
    }

    public GitSessionState MarkRejected()
    {
        return this with { RejectedAt = DateTimeOffset.UtcNow };
    }

    public GitSessionState MarkFailed(string reason)
    {
        return this with
        {
            IsFailed = true,
            FailureReason = reason,
            LastActivityAt = DateTimeOffset.UtcNow
        };
    }

    public GitSessionState UpdateActivity()
    {
        return this with { LastActivityAt = DateTimeOffset.UtcNow };
    }
}

public record GitToolCallCommit
{
    public required string ToolName { get; init; }
    public required string CommitHash { get; init; }
    public required DateTimeOffset CommittedAt { get; init; }
    public List<string> FilesChanged { get; init; } = new();
}
