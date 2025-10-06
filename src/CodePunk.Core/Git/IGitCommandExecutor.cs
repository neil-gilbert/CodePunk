namespace CodePunk.Core.Git;

public interface IGitCommandExecutor
{
    Task<GitOperationResult> ExecuteAsync(
        string arguments,
        CancellationToken cancellationToken = default,
        string? workingDirectory = null);

    Task<GitOperationResult<string>> GetCurrentBranchAsync(CancellationToken cancellationToken = default);

    Task<GitOperationResult<bool>> IsGitRepositoryAsync(CancellationToken cancellationToken = default);

    Task<GitOperationResult<List<string>>> GetModifiedFilesAsync(CancellationToken cancellationToken = default);

    Task<GitOperationResult<bool>> HasUncommittedChangesAsync(CancellationToken cancellationToken = default);

    Task<GitOperationResult<bool>> HasStagedChangesAsync(CancellationToken cancellationToken = default);
}
