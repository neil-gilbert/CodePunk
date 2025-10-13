using System.Text.Json;
using CodePunk.Core.Services;

namespace CodePunk.Core.Tools.Modes;

public class PlanModeTool : ITool
{
    public string Name => "mode_plan";

    public string Description => "Activate PLAN mode for new work/features. Provide a concise goal.";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            goal = new { type = "string", description = "The high-level feature or change goal" },
            rationale = new { type = "string", description = "Optional brief rationale or constraints" }
        },
        required = new[] { "goal" }
    });

    public Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetProperty("goal", out var goalEl) || goalEl.ValueKind != JsonValueKind.String)
        {
            return Task.FromResult(new ToolResult { Content = "Missing required parameter: goal", IsError = true, ErrorMessage = "goal is required" });
        }
        var goal = goalEl.GetString() ?? string.Empty;
        var rationale = arguments.TryGetProperty("rationale", out var rEl) && rEl.ValueKind == JsonValueKind.String ? rEl.GetString() : null;

        var content = $"PLAN mode activated.\nGoal: {goal}\n" +
                      (string.IsNullOrWhiteSpace(rationale) ? string.Empty : $"Rationale: {rationale}\n") +
                      "Next steps:\n" +
                      "1) Call plan_generate_ai with the goal to draft a change plan.\n" +
                      "2) Review proposed files and rationale.\n" +
                      "3) Inspect current files (read_file/search/glob).\n" +
                      "4) Apply diffs incrementally with write_file and approvals.";
        return Task.FromResult(new ToolResult { Content = content });
    }
}

