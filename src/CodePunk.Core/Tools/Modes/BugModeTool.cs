using System.Text.Json;
using CodePunk.Core.Services;

namespace CodePunk.Core.Tools.Modes;

public class BugModeTool : ITool
{
    public string Name => "mode_bug";

    public string Description => "Activate BUG mode for triage and fixes. Provide a brief description.";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            description = new { type = "string", description = "Bug description or observed behavior" },
            suspected_files = new { type = "array", items = new { type = "string" }, description = "Optional list of suspect paths" }
        },
        required = new[] { "description" }
    });

    public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetProperty("description", out var descEl) || descEl.ValueKind != JsonValueKind.String)
        {
            return Task.FromResult(new ToolResult { Content = "Missing required parameter: description", IsError = true, ErrorMessage = "description is required" });
        }
        var description = descEl.GetString() ?? string.Empty;
        var suspects = new List<string>();
        if (arguments.TryGetProperty("suspected_files", out var sEl) && sEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in sEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String) suspects.Add(item.GetString() ?? string.Empty);
            }
        }

        var content = "BUG mode activated.\n" +
                      $"Description: {description}\n" +
                      (suspects.Count > 0 ? $"Suspected files:\n- {string.Join("\n- ", suspects)}\n" : string.Empty) +
                      "Triage checklist:\n" +
                      "- Reproduce: run tests or commands (shell).\n" +
                      "- Locate: search logs/stack traces; use search_file_content and read_file.\n" +
                      "- Fix: propose minimal change, then write_file with diff-aware approach.\n" +
                      "- Verify: rerun tests/commands; consider edge cases.";
        return Task.FromResult(new ToolResult { Content = content });
    }
}

