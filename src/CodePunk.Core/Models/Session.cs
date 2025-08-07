using System.ComponentModel.DataAnnotations;

namespace CodePunk.Core.Models;

/// <summary>
/// Represents a chat session with the AI assistant
/// </summary>
public record Session
{
    public required string Id { get; init; }
    public string? ParentSessionId { get; init; }
    
    [Required, StringLength(200)]
    public required string Title { get; init; }
    
    public int MessageCount { get; init; }
    public long PromptTokens { get; init; }
    public long CompletionTokens { get; init; }
    public decimal Cost { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string? SummaryMessageId { get; init; }

    /// <summary>
    /// Creates a new session with generated ID and timestamps
    /// </summary>
    public static Session Create(string title, string? parentSessionId = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new Session
        {
            Id = Guid.NewGuid().ToString(),
            ParentSessionId = parentSessionId,
            Title = title,
            MessageCount = 0,
            PromptTokens = 0,
            CompletionTokens = 0,
            Cost = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
