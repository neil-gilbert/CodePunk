using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CodePunk.Core.Abstractions;

public class PlanFileSummary
{
    public required string Path { get; set; }
    public bool IsDelete { get; set; }
    public string? Rationale { get; set; }
    public bool Generated { get; set; }
    public List<string>? Diagnostics { get; set; }
}

public class PlanGenerationSummary
{
    public required string PlanId { get; set; }
    public required string Goal { get; set; }
    public required string Provider { get; set; }
    public required string Model { get; set; }
    public required List<PlanFileSummary> Files { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public interface IPlanService
{
    Task<PlanGenerationSummary> GenerateAsync(string goal, string? provider, string? model, CancellationToken ct = default);
}

