using CodePunk.Roslyn.Abstractions;
using CodePunk.Roslyn.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;

namespace CodePunk.Roslyn.Services;

public sealed class RoslynAnalyzerService : IRoslynAnalyzerService
{
    private readonly IRoslynWorkspaceService _workspace;

    public RoslynAnalyzerService(IRoslynWorkspaceService workspace)
    {
        _workspace = workspace;
    }

    public async Task<RoslynDiagnosticsResult> AnalyzeAsync(RoslynAnalyzeOptions options, CancellationToken ct = default)
    {
        await _workspace.InitializeAsync(options.SolutionPath, ct);
        var solution = await _workspace.GetSolutionAsync(ct);

        var result = new RoslynDiagnosticsResult();
        int cap = Math.Max(1, options.MaxItems);

        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation == null) continue;
            var diags = compilation.GetDiagnostics(ct);
            foreach (var d in diags)
            {
                if (d.Severity == DiagnosticSeverity.Error) result.ErrorCount++;
                else if (d.Severity == DiagnosticSeverity.Warning) result.WarningCount++;
                else result.InfoCount++;

                if (result.Items.Count < cap)
                {
                    var file = d.Location.GetLineSpan();
                    result.Items.Add(new RoslynDiagnosticItem
                    {
                        Id = d.Id,
                        Severity = d.Severity.ToString(),
                        Message = d.GetMessage(),
                        File = file.Path,
                        Line = file.StartLinePosition.Line + 1,
                        Column = file.StartLinePosition.Character + 1
                    });
                }
            }
        }

        return result;
    }

    public async Task<RoslynSymbolInfo> ExplainSymbolAsync(RoslynSymbolQuery query, int? maxReferences = null, CancellationToken ct = default)
    {
        await _workspace.InitializeAsync(null, ct);
        var solution = await _workspace.GetSolutionAsync(ct);
        var symbol = await _workspace.FindSymbolAsync(query, ct) as ISymbol;

        if (symbol == null && !string.IsNullOrWhiteSpace(query.FullyQualifiedName))
        {
            // Fall back: try match by simple name across global namespace
            foreach (var project in solution.Projects)
            {
                var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
                if (compilation == null) continue;
                var candidate = compilation.GlobalNamespace.GetMembers().FirstOrDefault(m => string.Equals(m.Name, query.FullyQualifiedName, StringComparison.Ordinal));
                if (candidate != null) { symbol = candidate; break; }
            }
        }

        var info = new RoslynSymbolInfo();
        if (symbol == null)
        {
            info.Name = query.FullyQualifiedName ?? (query.FilePath ?? "<unknown>");
            info.Kind = "Unknown";
            return info;
        }

        info.Name = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        info.Kind = symbol.Kind.ToString();
        info.ContainingType = symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        info.ContainingNamespace = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        info.Signature = symbol switch
        {
            IMethodSymbol m => m.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            IPropertySymbol p => p.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            IFieldSymbol f => f.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            _ => symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
        };
        try { info.SummaryXml = symbol.GetDocumentationCommentXml(cancellationToken: ct); } catch { }

        foreach (var loc in symbol.Locations)
        {
            if (!loc.IsInSource) continue;
            var span = loc.GetLineSpan();
            info.Locations.Add($"{span.Path}:{span.StartLinePosition.Line + 1}:{span.StartLinePosition.Character + 1}");
        }

        int cap = Math.Max(1, maxReferences ?? 100);
        try
        {
            var refs = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken: ct).ConfigureAwait(false);
            foreach (var r in refs)
            {
                foreach (var loc in r.Locations)
                {
                    if (info.References.Count >= cap) break;
                    var lspan = loc.Location.GetLineSpan();
                    info.References.Add($"{lspan.Path}:{lspan.StartLinePosition.Line + 1}:{lspan.StartLinePosition.Character + 1}");
                }
                if (info.References.Count >= cap) break;
            }
        }
        catch
        {
            // Ignore reference errors; keep basic info.
        }

        return info;
    }

    public async Task<RoslynCallGraphResult> BuildCallGraphAsync(RoslynSymbolQuery query, int? maxNodes = null, int depth = 1, CancellationToken ct = default)
    {
        await _workspace.InitializeAsync(null, ct);
        var solution = await _workspace.GetSolutionAsync(ct);
        var symbol = await _workspace.FindSymbolAsync(query, ct).ConfigureAwait(false) as ISymbol;
        var result = new RoslynCallGraphResult();
        if (symbol == null)
        {
            result.Root = new RoslynCallGraphNode { Id = "<unknown>", Name = query.FullyQualifiedName ?? (query.FilePath ?? "<unknown>"), Kind = "Unknown" };
            return result;
        }
        result.Root = new RoslynCallGraphNode
        {
            Id = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            Name = symbol.Name,
            Kind = symbol.Kind.ToString(),
            Location = symbol.Locations.FirstOrDefault(l => l.IsInSource)?.GetLineSpan().ToString()
        };

        int cap = Math.Max(1, maxNodes ?? 200);

        var outbound = new HashSet<string>();
        var inbound = new HashSet<string>();
        if (symbol is IMethodSymbol methodRoot)
        {
            await CollectOutboundAsync(solution, methodRoot, result.Calls, result.Edges, outbound, cap, depth, 20, ct).ConfigureAwait(false);
        }
        await CollectInboundAsync(solution, symbol, result.Callers, result.Edges, inbound, cap, depth, 20, ct).ConfigureAwait(false);

        // Deduplicate by Id
        result.Calls = result.Calls.GroupBy(n => n.Id).Select(g => g.First()).ToList();
        result.Callers = result.Callers.GroupBy(n => n.Id).Select(g => g.First()).ToList();
        result.Truncated = result.Calls.Count >= cap || result.Callers.Count >= cap;
        return result;
    }

    private static async Task CollectOutboundAsync(Solution solution, IMethodSymbol method, List<RoslynCallGraphNode> sink, List<RoslynCallGraphEdge> edges, HashSet<string> visited, int cap, int depth, int breadthLimit, CancellationToken ct)
    {
        if (depth <= 0) return;
        int breadth = 0;
        foreach (var decl in method.DeclaringSyntaxReferences)
        {
            var node = await decl.GetSyntaxAsync(ct).ConfigureAwait(false);
            var doc = solution.GetDocument(node.SyntaxTree);
            if (doc == null) continue;
            var model = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (model == null) continue;
            var invocations = node.DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (var inv in invocations)
            {
                if (sink.Count >= cap) return;
                if (breadth >= breadthLimit) break;
                var called = model.GetSymbolInfo(inv, ct).Symbol as IMethodSymbol;
                if (called == null) continue;
                var id = called.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                if (!visited.Add(id)) continue;
                var span = called.Locations.FirstOrDefault(l => l.IsInSource)?.GetLineSpan();
                sink.Add(new RoslynCallGraphNode
                {
                    Id = id,
                    Name = called.Name,
                    Kind = "Method",
                    Location = span?.ToString()
                });
                // Edge from current method to callee
                edges.Add(new RoslynCallGraphEdge { From = method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), To = id });
                breadth++;
                if (depth > 1)
                {
                    await CollectOutboundAsync(solution, called, sink, edges, visited, cap, depth - 1, breadthLimit, ct).ConfigureAwait(false);
                }
                if (sink.Count >= cap) return;
            }
        }
    }

    private static async Task CollectInboundAsync(Solution solution, ISymbol symbol, List<RoslynCallGraphNode> sink, List<RoslynCallGraphEdge> edges, HashSet<string> visited, int cap, int depth, int breadthLimit, CancellationToken ct)
    {
        if (depth <= 0) return;
        try
        {
            var callers = await SymbolFinder.FindCallersAsync(symbol, solution, cancellationToken: ct).ConfigureAwait(false);
            int breadth = 0;
            foreach (var c in callers)
            {
                if (sink.Count >= cap) return;
                if (breadth >= breadthLimit) break;
                var target = c.CallingSymbol;
                var id = target.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                if (!visited.Add(id)) continue;
                var span = target.Locations.FirstOrDefault(l => l.IsInSource)?.GetLineSpan();
                sink.Add(new RoslynCallGraphNode
                {
                    Id = id,
                    Name = target.Name,
                    Kind = target.Kind.ToString(),
                    Location = span?.ToString()
                });
                // Edge from caller to current symbol
                edges.Add(new RoslynCallGraphEdge { From = id, To = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) });
                breadth++;
                if (depth > 1)
                {
                    await CollectInboundAsync(solution, target, sink, edges, visited, cap, depth - 1, breadthLimit, ct).ConfigureAwait(false);
                }
            }
        }
        catch { }
    }
}
