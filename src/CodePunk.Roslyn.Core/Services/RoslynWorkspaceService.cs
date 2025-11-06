using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using CodePunk.Roslyn.Abstractions;
using CodePunk.Roslyn.Models;

namespace CodePunk.Roslyn.Services;

public sealed class RoslynWorkspaceService : IRoslynWorkspaceService, IDisposable
{
    private readonly object _gate = new();
    private MSBuildWorkspace? _workspace;
    private string? _loadedPath;
    private bool _disposed;

    static RoslynWorkspaceService()
    {
        try { if (!MSBuildLocator.IsRegistered) MSBuildLocator.RegisterDefaults(); } catch { }
    }

    public async Task InitializeAsync(string? slnOrProjectPath, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var path = ResolvePath(slnOrProjectPath);
        if (path == null) throw new InvalidOperationException("No .sln or .csproj could be discovered in current directory.");
        if (_workspace != null && string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase)) return;
        lock (_gate)
        {
            _workspace?.Dispose();
            _workspace = MSBuildWorkspace.Create();
            _loadedPath = path;
        }
        if (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            await _workspace!.OpenSolutionAsync(path, cancellationToken: ct).ConfigureAwait(false);
        }
        else
        {
            await _workspace!.OpenProjectAsync(path, cancellationToken: ct).ConfigureAwait(false);
        }
    }

    public Task<Solution> GetSolutionAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (_workspace == null) throw new InvalidOperationException("Workspace not initialized");
        return Task.FromResult(_workspace.CurrentSolution);
    }

    public async Task<(Document? Document, SemanticModel? Model)> GetDocumentModelAsync(string path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (_workspace == null) throw new InvalidOperationException("Workspace not initialized");
        var doc = _workspace.CurrentSolution.Projects.SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.FilePath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
        if (doc == null) return (null, null);
        var model = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
        return (doc, model);
    }

    public async Task<ISymbol?> FindSymbolAsync(RoslynSymbolQuery query, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (_workspace == null) throw new InvalidOperationException("Workspace not initialized");
        var solution = _workspace.CurrentSolution;

        if (!string.IsNullOrWhiteSpace(query.FilePath) && query.Line.HasValue && query.Column.HasValue)
        {
            var (doc, _) = await GetDocumentModelAsync(query.FilePath!, ct).ConfigureAwait(false);
            if (doc != null)
            {
                var text = await doc.GetTextAsync(ct).ConfigureAwait(false);
                var line = Math.Max(0, Math.Min(text.Lines.Count - 1, query.Line!.Value - 1));
                var col = Math.Max(0, query.Column!.Value - 1);
                var pos = text.Lines[line].Start + col;
                var root = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (root != null)
                {
                    var token = root.FindToken(pos);
                    var node = token.Parent;
                    var model = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
                    var declared = model?.GetDeclaredSymbol(node!, ct);
                    var symbol = declared ?? model?.GetSymbolInfo(node!, ct).Symbol;
                    if (symbol != null) return symbol;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(query.FullyQualifiedName))
        {
            var fqn = query.FullyQualifiedName!.Trim();
            // Try exact type metadata name first
            foreach (var project in solution.Projects)
            {
                var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
                if (compilation == null) continue;
                var type = compilation.GetTypeByMetadataName(fqn);
                if (type != null) return type;
            }

            // Try container + member: Namespace.Type.Member or Namespace.Type.Nested.Member
            var lastDot = fqn.LastIndexOf('.') ;
            if (lastDot > 0)
            {
                var container = fqn.Substring(0, lastDot);
                var member = fqn.Substring(lastDot + 1);
                // Handle optional parameter list (Foo(int,string)) → name=Foo
                var parenIdx = member.IndexOf('(');
                var memberName = parenIdx > 0 ? member.Substring(0, parenIdx) : member;
                foreach (var project in solution.Projects)
                {
                    var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
                    if (compilation == null) continue;
                    var type = compilation.GetTypeByMetadataName(container);
                    if (type == null)
                    {
                        // Fallback: scan for type by full name with '.' nesting
                        type = compilation.GlobalNamespace.GetNamespaceTypesRecursive().FirstOrDefault(t =>
                        {
                            var full = t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                            if (full.StartsWith("global::", StringComparison.Ordinal)) full = full.Substring("global::".Length);
                            return full.Equals(container, StringComparison.Ordinal);
                        });
                    }
                    if (type != null)
                    {
                        var members = type.GetMembers(memberName);
                        if (members.Length > 0) return members[0];
                    }
                }
            }

            // Broad fallback: search declarations by simple name
            var simple = fqn.Split('.').Last();
            foreach (var project in solution.Projects)
            {
                var decls = await Microsoft.CodeAnalysis.FindSymbols.SymbolFinder.FindDeclarationsAsync(project, simple, ignoreCase: false, cancellationToken: ct).ConfigureAwait(false);
                var found = decls.FirstOrDefault();
                if (found != null) return found;
            }
        }
        return null;
    }

    private static string? ResolvePath(string? proposed)
    {
        if (!string.IsNullOrWhiteSpace(proposed))
        {
            var p = Path.GetFullPath(proposed!);
            if (File.Exists(p)) return p;
        }
        var cwd = Directory.GetCurrentDirectory();
        var sln = Directory.EnumerateFiles(cwd, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (sln != null) return Path.GetFullPath(sln);
        var proj = Directory.EnumerateFiles(cwd, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (proj != null) return Path.GetFullPath(proj);
        return null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RoslynWorkspaceService));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _workspace?.Dispose();
    }
}

internal static class RoslynTypeEnumerationExtensions
{
    public static IEnumerable<INamedTypeSymbol> GetNamespaceTypesRecursive(this INamespaceSymbol ns)
    {
        foreach (var t in ns.GetTypeMembers()) yield return t;
        foreach (var child in ns.GetNamespaceMembers())
        {
            foreach (var t in GetNamespaceTypesRecursive(child)) yield return t;
        }
    }
}
