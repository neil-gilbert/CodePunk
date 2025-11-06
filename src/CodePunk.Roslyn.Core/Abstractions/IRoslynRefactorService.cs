using CodePunk.Roslyn.Models;

namespace CodePunk.Roslyn.Abstractions;

public interface IRoslynRefactorService
{
    Task<RoslynEditBatch> RenameSymbolAsync(RoslynRenameArgs args, CancellationToken ct = default);
    Task<RoslynEditBatch> ApplyCodeFixAsync(RoslynCodeFixArgs args, CancellationToken ct = default);
}
