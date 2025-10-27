using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodePunk.Core.Models;

public record Message
{
    public required string Id { get; init; }
    public required string SessionId { get; init; }
    public required MessageRole Role { get; init; }
    public required IReadOnlyList<MessagePart> Parts { get; init; } = [];
    public string? Model { get; init; }
    public string? Provider { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }

    public static Message Create(
        string sessionId,
        MessageRole role,
        IReadOnlyList<MessagePart> parts,
        string? model = null,
        string? provider = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new Message
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = sessionId,
            Role = role,
            Parts = parts,
            Model = model,
            Provider = provider,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}

public enum MessageRole
{
    User,
    Assistant,
    System,
    Tool
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextPart), "text")]
[JsonDerivedType(typeof(ToolCallPart), "tool_call")]
[JsonDerivedType(typeof(ToolResultPart), "tool_result")]
[JsonDerivedType(typeof(ImagePart), "image")]
public abstract record MessagePart
{
    public abstract MessagePartType Type { get; }
}

public record TextPart(string Content) : MessagePart
{
    public override MessagePartType Type => MessagePartType.Text;
}

public record ToolCallPart(
    string Id,
    string Name,
    JsonElement Arguments) : MessagePart
{
    public override MessagePartType Type => MessagePartType.ToolCall;
}

public record ToolResultPart(
    string ToolCallId,
    string Content,
    bool IsError = false) : MessagePart
{
    public override MessagePartType Type => MessagePartType.ToolResult;
}

public record ImagePart(
    string Url,
    string? Description = null) : MessagePart
{
    public override MessagePartType Type => MessagePartType.Image;
}

public enum MessagePartType
{
    Text,
    ToolCall,
    ToolResult,
    Image
}

