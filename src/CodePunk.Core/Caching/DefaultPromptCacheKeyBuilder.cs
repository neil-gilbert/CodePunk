using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;

namespace CodePunk.Core.Caching;

/// <summary>
/// Builds deterministic prompt cache keys.
/// </summary>
public sealed class DefaultPromptCacheKeyBuilder : IPromptCacheKeyBuilder
{
    public PromptCacheKey Build(PromptCacheContext context)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { SkipValidation = true }))
        {
            WriteDocument(writer, context);
        }

        var hash = SHA256.HashData(buffer.WrittenSpan);
        var key = Convert.ToHexString(hash).ToLowerInvariant();
        return new PromptCacheKey(key);
    }

    private static void WriteDocument(Utf8JsonWriter writer, PromptCacheContext context)
    {
        var request = context.Request;

        writer.WriteStartObject();
        writer.WriteString("provider", context.ProviderName);
        // Build a cache key based solely on the system prompt text and provider.
        // This maximizes reuse of provider-side system prompt caches across turns, regardless of messages/tools.
        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            writer.WriteString("systemPrompt", request.SystemPrompt);
        }

        writer.WriteEndObject();
    }

    private static void WriteMessage(Utf8JsonWriter writer, Message message)
    {
        writer.WriteStartObject();
        writer.WriteString("role", message.Role.ToString());
        writer.WritePropertyName("parts");
        writer.WriteStartArray();

        foreach (var part in message.Parts ?? Array.Empty<MessagePart>())
        {
            writer.WriteStartObject();
            writer.WriteString("type", part.Type.ToString());

            switch (part)
            {
                case TextPart text:
                    writer.WriteString("text", text.Content);
                    break;
                case ToolCallPart call:
                    writer.WriteString("id", call.Id);
                    writer.WriteString("name", call.Name);
                    writer.WritePropertyName("arguments");
                    WriteCanonicalJson(writer, call.Arguments);
                    break;
                case ToolResultPart result:
                    writer.WriteString("toolCallId", result.ToolCallId);
                    writer.WriteString("content", result.Content);
                    writer.WriteBoolean("isError", result.IsError);
                    break;
                case ImagePart image:
                    writer.WriteString("url", image.Url);
                    if (!string.IsNullOrEmpty(image.Description))
                    {
                        writer.WriteString("description", image.Description);
                    }
                    break;
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteTool(Utf8JsonWriter writer, LLMTool tool)
    {
        writer.WriteStartObject();
        writer.WriteString("name", tool.Name);
        writer.WriteString("description", tool.Description);
        writer.WritePropertyName("parameters");
        WriteCanonicalJson(writer, tool.Parameters);
        writer.WriteEndObject();
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalJson(writer, item);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var intValue))
                {
                    writer.WriteNumberValue(intValue);
                }
                else if (element.TryGetDecimal(out var decimalValue))
                {
                    writer.WriteNumberValue(decimalValue);
                }
                else if (element.TryGetDouble(out var doubleValue))
                {
                    writer.WriteNumberValue(doubleValue);
                }
                else
                {
                    writer.WriteRawValue(element.GetRawText());
                }
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                writer.WriteBooleanValue(element.GetBoolean());
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
        }
    }
}
