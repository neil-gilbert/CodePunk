using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodePunk.Core.Abstractions;

namespace CodePunk.Console.Planning;

public class ConsolePlanServiceAdapter : IPlanService
{
    private readonly IPlanAiGenerationService _ai;

    public ConsolePlanServiceAdapter(IPlanAiGenerationService ai)
    {
        _ai = ai;
    }

    public async Task<PlanGenerationSummary> GenerateAsync(string goal, string? provider, string? model, CancellationToken ct = default)
    {
        var res = await _ai.GenerateAsync(goal, provider, model, ct);
        var files = res.Files.Select(f => new PlanFileSummary
        {
            Path = f.Path,
            IsDelete = f.IsDelete,
            Rationale = f.Rationale,
            Generated = f.Generated ?? false,
            Diagnostics = f.Diagnostics?.ToList()
        }).ToList();
        return new PlanGenerationSummary
        {
            PlanId = res.PlanId,
            Goal = res.Goal,
            Provider = res.Provider,
            Model = res.Model,
            Files = files,
            ErrorCode = res.ErrorCode,
            ErrorMessage = res.ErrorMessage
        };
    }
}
