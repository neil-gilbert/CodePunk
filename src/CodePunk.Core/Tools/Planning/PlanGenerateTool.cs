using System.Text.Json;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CodePunk.Core.Tools.Planning;

public class PlanGenerateTool : ITool
{
    private readonly IServiceProvider _services;

    public PlanGenerateTool(IServiceProvider services)
    {
        _services = services;
    }

    public string Name => "plan_generate_ai";

    public string Description => "Generate a multi-file change plan from a natural language goal.";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            goal = new { type = "string", description = "The desired change goal" },
            provider = new { type = "string", description = "Optional provider override" },
            model = new { type = "string", description = "Optional model override" }
        },
        required = new[] { "goal" }
    });

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetProperty("goal", out var goalEl) || goalEl.ValueKind != JsonValueKind.String)
        {
            return new ToolResult { Content = "Missing required parameter: goal", IsError = true, ErrorMessage = "goal is required" };
        }
        var goal = goalEl.GetString() ?? string.Empty;
        var provider = arguments.TryGetProperty("provider", out var provEl) && provEl.ValueKind == JsonValueKind.String ? provEl.GetString() : null;
        var model = arguments.TryGetProperty("model", out var modelEl) && modelEl.ValueKind == JsonValueKind.String ? modelEl.GetString() : null;

        try
        {
            using var scope = _services.CreateScope();
            var planService = scope.ServiceProvider.GetRequiredService<IPlanService>();
            var summary = await planService.GenerateAsync(goal, provider, model, cancellationToken);
            var payload = new
            {
                planId = summary.PlanId,
                goal = summary.Goal,
                provider = summary.Provider,
                model = summary.Model,
                files = summary.Files.Select(f => new
                {
                    path = f.Path,
                    isDelete = f.IsDelete,
                    rationale = f.Rationale,
                    generated = f.Generated,
                    diagnostics = f.Diagnostics
                }).ToList(),
                errorCode = summary.ErrorCode,
                errorMessage = summary.ErrorMessage
            };
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            return new ToolResult { Content = json };
        }
        catch (Exception ex)
        {
            return new ToolResult { Content = $"plan_generate_ai failed: {ex.Message}", IsError = true, ErrorMessage = ex.Message };
        }
    }
}
