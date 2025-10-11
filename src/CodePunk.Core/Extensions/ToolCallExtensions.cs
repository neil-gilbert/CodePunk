using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodePunk.Core.Models;

namespace CodePunk.Core.Extensions;

/// <summary>
/// Helper methods for extracting metadata from tool call arguments.
/// </summary>
public static class ToolCallExtensions
{
    public static string? GetFilePath(this ToolCallPart toolCall)
    {
        try
        {
            var arguments = toolCall.Arguments;
            if (arguments.ValueKind != JsonValueKind.Object)
                return null;

            if (arguments.TryGetProperty("path", out var pathElement) && pathElement.ValueKind == JsonValueKind.String)
                return pathElement.GetString();

            if (arguments.TryGetProperty("file_path", out var filePathElement) && filePathElement.ValueKind == JsonValueKind.String)
                return filePathElement.GetString();

            return null;
        }
        catch
        {
            return null;
        }
    }

    public static string GetStableSignature(this ToolCallPart toolCall)
    {
        try
        {
            var serializedArgs = toolCall.Arguments.ValueKind == JsonValueKind.Undefined || toolCall.Arguments.ValueKind == JsonValueKind.Null
                ? "{}"
                : JsonSerializer.Serialize(toolCall.Arguments);

            var buffer = Encoding.UTF8.GetBytes($"{toolCall.Name}:{serializedArgs}");
            var hash = SHA256.HashData(buffer);
            return Convert.ToHexString(hash);
        }
        catch
        {
            return toolCall.Name;
        }
    }
}
