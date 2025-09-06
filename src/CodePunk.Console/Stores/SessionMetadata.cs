namespace CodePunk.Console.Stores;

public class SessionMetadata
{
    public string Id { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Agent { get; set; }
    public string? Model { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
}

public class SessionMessageRecord
{
    public string Role { get; set; } = string.Empty; // user | assistant | tool
    public string Content { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}

public class SessionRecord
{
    public SessionMetadata Metadata { get; set; } = new();
    public List<SessionMessageRecord> Messages { get; set; } = new();
}
