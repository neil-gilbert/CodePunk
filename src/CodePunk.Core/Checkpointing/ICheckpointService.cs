namespace CodePunk.Core.Checkpointing;

public interface ICheckpointService
{
    Task<CheckpointResult> InitializeAsync(string workspacePath, CancellationToken cancellationToken = default);

    Task<CheckpointResult<string>> CreateCheckpointAsync(
        string toolCallId,
        string toolName,
        string description,
        CancellationToken cancellationToken = default);

    Task<CheckpointResult> RestoreCheckpointAsync(
        string checkpointId,
        CancellationToken cancellationToken = default);

    Task<CheckpointResult<IReadOnlyList<CheckpointMetadata>>> ListCheckpointsAsync(
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<CheckpointResult<CheckpointMetadata>> GetCheckpointAsync(
        string checkpointId,
        CancellationToken cancellationToken = default);

    Task<CheckpointResult> PruneCheckpointsAsync(
        int keepCount = 100,
        CancellationToken cancellationToken = default);
}
