using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using CodePunk.Core.Services;
using CodePunk.Core.Utils;
using CodePunk.Roslyn.Abstractions;
using CodePunk.Roslyn.Models;

namespace CodePunk.Roslyn.Tools;

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

    public JsonElement Parameters => JsonSchemaGenerator.Generate<ExplainSymbolArgs>();

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (!ToolArgumentBinder.TryBindAndValidate<ExplainSymbolArgs>(arguments, out var args, out var error))
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

            var info = await _analyzer.ExplainSymbolAsync(query, args.MaxReferences, cancellationToken);

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

