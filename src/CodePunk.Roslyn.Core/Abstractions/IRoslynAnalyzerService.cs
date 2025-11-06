using CodePunk.Roslyn.Models;

namespace CodePunk.Roslyn.Abstractions;

public interface IRoslynAnalyzerService
{
    Task<RoslynDiagnosticsResult> AnalyzeAsync(RoslynAnalyzeOptions options, CancellationToken ct = default);
    Task<RoslynSymbolInfo> ExplainSymbolAsync(RoslynSymbolQuery query, int? maxReferences = null, CancellationToken ct = default);
    Task<RoslynCallGraphResult> BuildCallGraphAsync(RoslynSymbolQuery query, int? maxNodes = null, int depth = 1, CancellationToken ct = default);
}
