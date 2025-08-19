namespace CodePunk.Core.Chat;

/// <summary>
/// Stream chunk specifically for chat sessions with enhanced metadata
/// </summary>
public record ChatStreamChunk
{
    public string? ContentDelta { get; init; }
    public string? Model { get; init; }
    public string? Provider { get; init; }
    public bool IsComplete { get; init; }
}
