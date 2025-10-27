using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodePunk.Core.Models;

namespace CodePunk.Core.Abstractions;

// Models are now under CodePunk.Core/Plan/Models

public interface IPlanService
{
    Task<PlanGenerationSummary> GenerateAsync(string goal, string? provider, string? model, CancellationToken ct = default);
}
