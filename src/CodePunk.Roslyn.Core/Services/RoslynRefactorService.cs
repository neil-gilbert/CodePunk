using CodePunk.Roslyn.Abstractions;
using CodePunk.Roslyn.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodePunk.Roslyn.Services;

public sealed class RoslynRefactorService : IRoslynRefactorService
{
    private readonly IRoslynWorkspaceService _workspace;

    public RoslynRefactorService(IRoslynWorkspaceService workspace)
    {
        _workspace = workspace;
    }

    public async Task<RoslynEditBatch> RenameSymbolAsync(RoslynRenameArgs args, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(args.NewName)) throw new ArgumentException("NewName is required", nameof(args));
        await _workspace.InitializeAsync(null, ct);
        var solution = await _workspace.GetSolutionAsync(ct).ConfigureAwait(false);
        var symbol = await _workspace.FindSymbolAsync(args.Target, ct).ConfigureAwait(false);
        if (symbol == null) throw new InvalidOperationException("Target symbol not found");

        // Use legacy OptionSet overload (supported in Roslyn 4.*); suppresses obsolete warning in build output
        var newSolution = await Renamer.RenameSymbolAsync(solution, symbol, args.NewName, solution.Options, ct).ConfigureAwait(false);
        var edits = await CollectEditsAsync(solution, newSolution, ct).ConfigureAwait(false);
        return new RoslynEditBatch(edits);
    }

    private static async Task<List<RoslynEdit>> CollectEditsAsync(Solution before, Solution after, CancellationToken ct)
    {
        var changes = after.GetChanges(before);
        var edits = new List<RoslynEdit>();
        foreach (var projChange in changes.GetProjectChanges())
        {
            foreach (var docId in projChange.GetChangedDocuments())
            {
                var oldDoc = projChange.OldProject.GetDocument(docId);
                var newDoc = projChange.NewProject.GetDocument(docId);
                if (oldDoc == null || newDoc == null) continue;
                var oldText = await oldDoc.GetTextAsync(ct).ConfigureAwait(false);
                var newText = await newDoc.GetTextAsync(ct).ConfigureAwait(false);
                var path = newDoc.FilePath ?? oldDoc.FilePath;
                if (string.IsNullOrEmpty(path)) continue;
                edits.Add(new RoslynEdit
                {
                    Path = path!,
                    Before = oldText.ToString(),
                    After = newText.ToString()
                });
            }
        }
        // Deduplicate by path (keep last)
        edits = edits.GroupBy(e => Path.GetFullPath(e.Path), StringComparer.OrdinalIgnoreCase).Select(g => g.Last()).ToList();
        return edits;
    }

    public async Task<RoslynEditBatch> ApplyCodeFixAsync(RoslynCodeFixArgs args, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(args.DiagnosticIdOrFixId)) throw new ArgumentException("DiagnosticIdOrFixId is required");
        await _workspace.InitializeAsync(null, ct);
        var solution = await _workspace.GetSolutionAsync(ct).ConfigureAwait(false);

        // Initial supported fixes: format, simplify names
        var id = args.DiagnosticIdOrFixId.Trim();

        if (string.Equals(id, "format", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "format-document", StringComparison.OrdinalIgnoreCase))
        {
            var after = solution;
            if (!string.IsNullOrWhiteSpace(args.FilePath))
            {
                var docPath = Path.GetFullPath(args.FilePath);
                var doc = solution.Projects.SelectMany(p => p.Documents).FirstOrDefault(d => string.Equals(d.FilePath, docPath, StringComparison.OrdinalIgnoreCase));
                if (doc != null)
                {
                    var formatted = await Formatter.FormatAsync(doc, cancellationToken: ct).ConfigureAwait(false);
                    after = formatted.Project.Solution;
                }
            }
            else
            {
                foreach (var project in solution.Projects)
                {
                    foreach (var doc in project.Documents)
                    {
                        var formatted = await Formatter.FormatAsync(doc, cancellationToken: ct).ConfigureAwait(false);
                        after = formatted.Project.Solution;
                    }
                }
            }
            var edits = await CollectEditsAsync(solution, after, ct).ConfigureAwait(false);
            return new RoslynEditBatch(edits);
        }

        if (string.Equals(id, "simplify-names", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "IDE0001", StringComparison.OrdinalIgnoreCase))
        {
            var after = solution;
            if (!string.IsNullOrWhiteSpace(args.FilePath))
            {
                var docPath = Path.GetFullPath(args.FilePath);
                var doc = solution.Projects.SelectMany(p => p.Documents).FirstOrDefault(d => string.Equals(d.FilePath, docPath, StringComparison.OrdinalIgnoreCase));
                if (doc != null)
                {
                    var simplified = await Simplifier.ReduceAsync(doc, cancellationToken: ct).ConfigureAwait(false);
                    simplified = await Formatter.FormatAsync(simplified, cancellationToken: ct).ConfigureAwait(false);
                    after = simplified.Project.Solution;
                }
            }
            else
            {
                foreach (var project in solution.Projects)
                {
                    foreach (var doc in project.Documents)
                    {
                        var simplified = await Simplifier.ReduceAsync(doc, cancellationToken: ct).ConfigureAwait(false);
                        simplified = await Formatter.FormatAsync(simplified, cancellationToken: ct).ConfigureAwait(false);
                        after = simplified.Project.Solution;
                    }
                }
            }
            var edits = await CollectEditsAsync(solution, after, ct).ConfigureAwait(false);
            return new RoslynEditBatch(edits);
        }

        if (string.Equals(id, "remove-unused-usings", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "CS8019", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "IDE0005", StringComparison.OrdinalIgnoreCase))
        {
            var after = solution;
            // Process a single file or all documents
            IEnumerable<Document> documents;
            if (!string.IsNullOrWhiteSpace(args.FilePath))
            {
                var docPath = Path.GetFullPath(args.FilePath);
                var doc = solution.Projects.SelectMany(p => p.Documents).FirstOrDefault(d => string.Equals(d.FilePath, docPath, StringComparison.OrdinalIgnoreCase));
                documents = doc != null ? new[] { doc } : Array.Empty<Document>();
            }
            else
            {
                documents = solution.Projects.SelectMany(p => p.Documents).ToList();
            }

            foreach (var doc in documents)
            {
                var project = doc.Project;
                var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
                if (compilation == null || doc.FilePath == null) continue;
                var tree = await doc.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
                if (tree == null) continue;
                var allDiags = compilation.GetDiagnostics(ct);
                var diags = allDiags.Where(d => (d.Id == "CS8019" || d.Id == "IDE0005") && d.Location.IsInSource && d.Location.SourceTree == tree).ToList();
                if (diags.Count == 0) continue;
                var root = (await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false)) as CSharpSyntaxNode;
                if (root == null) continue;
                var toRemove = new HashSet<UsingDirectiveSyntax>();
                foreach (var d in diags)
                {
                    var node = root.FindNode(d.Location.SourceSpan, getInnermostNodeForTie: true);
                    var usingNode = node.FirstAncestorOrSelf<UsingDirectiveSyntax>();
                    if (usingNode != null) toRemove.Add(usingNode);
                }
                if (toRemove.Count == 0) continue;
                var newRoot = root.RemoveNodes(toRemove, SyntaxRemoveOptions.KeepNoTrivia);
                var newDoc = doc.WithSyntaxRoot(newRoot);
                newDoc = await Formatter.FormatAsync(newDoc, cancellationToken: ct).ConfigureAwait(false);
                after = newDoc.Project.Solution;
            }

            var edits = await CollectEditsAsync(solution, after, ct).ConfigureAwait(false);
            return new RoslynEditBatch(edits);
        }

        if (string.Equals(id, "sort-usings", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "organize-usings", StringComparison.OrdinalIgnoreCase))
        {
            var after = solution;
            IEnumerable<Document> documents;
            if (!string.IsNullOrWhiteSpace(args.FilePath))
            {
                var docPath = Path.GetFullPath(args.FilePath);
                var doc = solution.Projects.SelectMany(p => p.Documents).FirstOrDefault(d => string.Equals(d.FilePath, docPath, StringComparison.OrdinalIgnoreCase));
                documents = doc != null ? new[] { doc } : Array.Empty<Document>();
            }
            else
            {
                documents = solution.Projects.SelectMany(p => p.Documents).ToList();
            }

            foreach (var doc in documents)
            {
                var root = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false) as CSharpSyntaxNode;
                if (root == null) continue;
                var updated = UsingSortRewriter.SortUsingsInTree(root);
                if (!ReferenceEquals(updated, root))
                {
                    var newDoc = doc.WithSyntaxRoot(updated);
                    newDoc = await Formatter.FormatAsync(newDoc, cancellationToken: ct).ConfigureAwait(false);
                    after = newDoc.Project.Solution;
                }
            }

            var edits = await CollectEditsAsync(solution, after, ct).ConfigureAwait(false);
            return new RoslynEditBatch(edits);
        }

        throw new NotSupportedException($"Code fix/transform for '{id}' is not supported yet.");
    }
}

internal static class UsingSorter
{
    public static SyntaxList<UsingDirectiveSyntax> Sort(SyntaxList<UsingDirectiveSyntax> list)
    {
        // Order: non-alias System.* first, then non-alias others, then alias usings; static before non-static within groups; then by name
        var ordered = list.OrderBy(u => IsAlias(u) ? 2 : 0)
                          .ThenBy(u => u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) ? 0 : 1)
                          .ThenBy(u => IsSystemUsing(u) ? 0 : 1)
                          .ThenBy(u => GetUsingName(u), StringComparer.Ordinal)
                          .ToList();
        return new SyntaxList<UsingDirectiveSyntax>(ordered);
    }

    private static bool IsAlias(UsingDirectiveSyntax u) => u.Alias != null;
    private static bool IsSystemUsing(UsingDirectiveSyntax u)
    {
        var name = u.Name?.ToString();
        return name != null && (name == "System" || name.StartsWith("System.", StringComparison.Ordinal));
    }
    private static string GetUsingName(UsingDirectiveSyntax u) => u.Alias?.Name.ToString() ?? u.Name?.ToString() ?? string.Empty;
}

internal static class UsingSortRewriter
{
    public static CSharpSyntaxNode SortUsingsInTree(CSharpSyntaxNode root)
    {
        var updated = root;
        // Top-level
        if (updated is CompilationUnitSyntax cu)
        {
            var sorted = UsingSorter.Sort(cu.Usings);
            if (!SyntaxListEqual(cu.Usings, sorted))
            {
                updated = cu.WithUsings(sorted);
            }
        }

        // Namespace declarations
        var namespaces = updated.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().ToList();
        foreach (var ns in namespaces)
        {
            var sorted = UsingSorter.Sort(ns.Usings);
            if (!SyntaxListEqual(ns.Usings, sorted))
            {
                var replaced = ns.WithUsings(sorted);
                updated = updated.ReplaceNode(ns, replaced);
            }
        }

        return updated;
    }

    private static bool SyntaxListEqual(SyntaxList<UsingDirectiveSyntax> a, SyntaxList<UsingDirectiveSyntax> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i].ToFullString(), b[i].ToFullString(), StringComparison.Ordinal)) return false;
        }
        return true;
    }
}
