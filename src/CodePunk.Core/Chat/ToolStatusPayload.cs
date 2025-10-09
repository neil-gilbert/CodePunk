using System.Text.Json;
using System.Text.Json.Serialization;
using CodePunk.Core.Models;

namespace CodePunk.Core.Chat;

/// <summary>
/// Structured payload describing a tool execution status update.
/// </summary>
public sealed record ToolStatusPayload(
    [property: JsonPropertyName("toolCallId")] string ToolCallId,
    [property: JsonPropertyName("toolName")] string ToolName,
    [property: JsonPropertyName("filePath")] string? FilePath,
    [property: JsonPropertyName("preview")] string Preview,
    [property: JsonPropertyName("isTruncated")] bool IsTruncated,
    [property: JsonPropertyName("originalLineCount")] int OriginalLineCount,
    [property: JsonPropertyName("maxLines")] int MaxLines,
    [property: JsonPropertyName("isError")] bool IsError,
    [property: JsonPropertyName("languageId")] string? LanguageId);

/// <summary>
/// Serialises and deserialises tool status payloads for streaming transport.
/// </summary>
public static class ToolStatusSerializer
{
    public const string Prefix = "tool-status::";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(ToolStatusPayload payload)
    {
        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        return Prefix + json;
    }

    public static bool TryDeserialize(string? text, out ToolStatusPayload? payload)
    {
        payload = null;
        if (string.IsNullOrEmpty(text))
            return false;

        if (!text.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var json = text[Prefix.Length..];
        try
        {
            payload = JsonSerializer.Deserialize<ToolStatusPayload>(json, SerializerOptions);
            return payload != null;
        }
        catch
        {
            payload = null;
            return false;
        }
    }
}
