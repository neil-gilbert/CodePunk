using Microsoft.CodeAnalysis;
using CodePunk.Roslyn.Models;

namespace CodePunk.Roslyn.Abstractions;

public interface IRoslynWorkspaceService
{
    Task InitializeAsync(string? slnOrProjectPath, CancellationToken ct = default);
    Task<Solution> GetSolutionAsync(CancellationToken ct = default);
    Task<(Document? Document, SemanticModel? Model)> GetDocumentModelAsync(string path, CancellationToken ct = default);
    Task<ISymbol?> FindSymbolAsync(RoslynSymbolQuery query, CancellationToken ct = default);
}
