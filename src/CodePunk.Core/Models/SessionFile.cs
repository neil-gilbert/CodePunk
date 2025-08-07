namespace CodePunk.Core.Models;

/// <summary>
/// Represents a file version tracked in a session
/// </summary>
public record SessionFile
{
    public required string Id { get; init; }
    public required string SessionId { get; init; }
    public required string Path { get; init; }
    public required string Content { get; init; }
    public long Version { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public static SessionFile Create(string sessionId, string path, string content, long version = 1)
    {
        var now = DateTimeOffset.UtcNow;
        return new SessionFile
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = sessionId,
            Path = path,
            Content = content,
            Version = version,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
