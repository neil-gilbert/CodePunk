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
    // Optional usage metrics surfaced once known (usually only on completion or provider-sent usage deltas)
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public int? TotalTokens => (InputTokens.HasValue || OutputTokens.HasValue) ? (InputTokens ?? 0) + (OutputTokens ?? 0) : null;
    public decimal? EstimatedCost { get; init; }
}
