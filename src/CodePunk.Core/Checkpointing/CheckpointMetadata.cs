namespace CodePunk.Core.Checkpointing;

public record CheckpointMetadata(
    string Id,
    string ToolCallId,
    string ToolName,
    string Description,
    DateTimeOffset CreatedAt,
    string CommitHash,
    IReadOnlyList<string> ModifiedFiles);
