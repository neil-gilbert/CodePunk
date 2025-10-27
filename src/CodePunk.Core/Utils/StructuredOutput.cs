using System.Text.Json;

namespace CodePunk.Core.Utils;

public static class StructuredOutput
{
    /// <summary>
    /// Attempt to parse a JSON object from a model response. Tries exact parse first,
    /// then attempts to extract the first top-level JSON object substring.
    /// </summary>
    public static bool TryParseJson<T>(string? content, out T? value, out string? error)
    {
        value = default;
        error = null;
        if (string.IsNullOrWhiteSpace(content))
        {
            error = "Empty content";
            return false;
        }

        // Attempt direct parse first
        try
        {
            value = JsonSerializer.Deserialize<T>(content!);
            if (value != null) return true;
        }
        catch { }

        // Try to find a JSON object substring
        var json = ExtractFirstJsonObject(content!);
        if (json == null)
        {
            error = "Unable to locate a JSON object in the response.";
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize<T>(json);
            return value != null;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string? ExtractFirstJsonObject(string text)
    {
        int depth = 0;
        int start = -1;
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '{')
            {
                if (depth == 0) start = i;
                depth++;
            }
            else if (c == '}')
            {
                if (depth > 0)
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        return text.Substring(start, i - start + 1);
                    }
                }
            }
        }
        return null;
    }
}

