// This is a non-compiled skeleton illustrating how a Roslyn tool would be wired.
// Place implementation in a new project: src/CodePunk.Roslyn.Tools (net9.0), reference CodePunk.Core + Roslyn Core.

using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using CodePunk.Core.Services;

namespace CodePunk.Roslyn.Tools
{
    // DTO used for parameter schema generation via JsonSchemaGenerator
    public sealed class ExplainSymbolArgs
    {
        [Display(Description = "Optional path to .sln or .csproj. Defaults to auto-discovery from CWD.")]
        public string? SolutionPath { get; set; }

        [Display(Description = "Fully-qualified symbol name (e.g., Acme.Core.MyType.DoWork). Optional if using location.")]
        public string? FullyQualifiedName { get; set; }

        [Display(Description = "Source file path for location-based lookup.")]
        public string? File { get; set; }

        [Display(Description = "1-based line number for location-based lookup.")]
        public int? Line { get; set; }

        [Display(Description = "1-based column number for location-based lookup.")]
        public int? Column { get; set; }

        [Display(Description = "Limit number of references returned (default 100)."), Range(1, 1000)]
        public int? MaxReferences { get; set; }
    }

    // Implement ITool in CodePunk style. Dependencies resolved via DI.
    public sealed class RoslynExplainSymbolTool : ITool
    {
        private readonly IRoslynWorkspaceService _workspace;
        private readonly IRoslynAnalyzerService _analyzer;

        public RoslynExplainSymbolTool(IRoslynWorkspaceService workspace, IRoslynAnalyzerService analyzer)
        {
            _workspace = workspace;
            _analyzer = analyzer;
        }

        public string Name => "roslyn_explain_symbol";

        public string Description => "Explain a C# symbol by name or location: signature, container, references, XML docs.";

        public JsonElement Parameters => CodePunk.Core.Utils.JsonSchemaGenerator.Generate<ExplainSymbolArgs>();

        public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
        {
            if (!CodePunk.Core.Utils.ToolArgumentBinder.TryBindAndValidate<ExplainSymbolArgs>(arguments, out var args, out var error))
            {
                return new ToolResult { Content = error ?? "Invalid arguments", IsError = true, ErrorMessage = error };
            }

            try
            {
                await _workspace.InitializeAsync(args!.SolutionPath, cancellationToken);

                var query = new RoslynSymbolQuery
                {
                    FullyQualifiedName = args.FullyQualifiedName,
                    FilePath = args.File,
                    Line = args.Line,
                    Column = args.Column
                };

                var info = await _analyzer.ExplainSymbolAsync(query, cancellationToken);

                var payload = new
                {
                    schema = "codepunk.roslyn.symbol.v1",
                    symbol = info
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                return new ToolResult { Content = json };
            }
            catch (OperationCanceledException)
            {
                return new ToolResult { Content = "Operation cancelled", IsError = true, ErrorMessage = "Cancelled" };
            }
            catch (Exception ex)
            {
                return new ToolResult { Content = $"Roslyn error: {ex.Message}", IsError = true, ErrorMessage = ex.Message };
            }
        }
    }
}

// Sketch of Roslyn service contracts (to place in CodePunk.Roslyn.Core)
namespace CodePunk.Roslyn
{
    public sealed class RoslynSymbolQuery
    {
        public string? FullyQualifiedName { get; set; }
        public string? FilePath { get; set; }
        public int? Line { get; set; }
        public int? Column { get; set; }
    }

    public sealed class RoslynSymbolInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string? Signature { get; set; }
        public string? ContainingType { get; set; }
        public string? ContainingNamespace { get; set; }
        public List<string> Locations { get; set; } = new();
        public List<string> References { get; set; } = new();
        public string? SummaryXml { get; set; }
    }

    public interface IRoslynWorkspaceService
    {
        Task InitializeAsync(string? slnOrProjectPath, CancellationToken ct);
        Task<Microsoft.CodeAnalysis.Solution> GetSolutionAsync(CancellationToken ct);
        Task<(Microsoft.CodeAnalysis.Document? Document, Microsoft.CodeAnalysis.SemanticModel? Model)> GetDocumentModelAsync(string path, CancellationToken ct);
        Task<Microsoft.CodeAnalysis.ISymbol?> FindSymbolAsync(RoslynSymbolQuery query, CancellationToken ct);
    }

    public interface IRoslynAnalyzerService
    {
        Task<RoslynSymbolInfo> ExplainSymbolAsync(RoslynSymbolQuery query, CancellationToken ct);
    }
}

