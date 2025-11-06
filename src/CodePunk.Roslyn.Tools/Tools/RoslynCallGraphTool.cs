using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using CodePunk.Core.Services;
using CodePunk.Core.Utils;
using CodePunk.Roslyn.Abstractions;
using CodePunk.Roslyn.Models;

namespace CodePunk.Roslyn.Tools;

public sealed class CallGraphArgs
{
    [Display(Description = "Fully-qualified symbol name (e.g., Namespace.Type.Method). Optional if using location.")]
    public string? FullyQualifiedName { get; set; }

    [Display(Description = "Source file path for location-based lookup.")]
    public string? File { get; set; }

    [Display(Description = "1-based line number for location-based lookup.")]
    public int? Line { get; set; }

    [Display(Description = "1-based column number for location-based lookup.")]
    public int? Column { get; set; }

    [Display(Description = "Maximum nodes to include (default 200)."), Range(1, 2000)]
    public int? MaxNodes { get; set; }

    [Display(Description = "Depth for transitive traversal (default 1 = direct only)."), Range(1, 5)]
    public int? Depth { get; set; }
}

public sealed class RoslynCallGraphTool : ITool
{
    private readonly IRoslynWorkspaceService _workspace;
    private readonly IRoslynAnalyzerService _analyzer;

    public RoslynCallGraphTool(IRoslynWorkspaceService workspace, IRoslynAnalyzerService analyzer)
    { _workspace = workspace; _analyzer = analyzer; }

    public string Name => "roslyn_call_graph";
    public string Description => "Build a local call graph (inbound/outbound) for a given method.";
    public JsonElement Parameters => JsonSchemaGenerator.Generate<CallGraphArgs>();

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (!ToolArgumentBinder.TryBindAndValidate<CallGraphArgs>(arguments, out var args, out var error))
        { return new ToolResult { Content = error ?? "Invalid arguments", IsError = true, ErrorMessage = error }; }
        try
        {
            await _workspace.InitializeAsync(null, cancellationToken);
            var query = new RoslynSymbolQuery { FullyQualifiedName = args!.FullyQualifiedName, FilePath = args.File, Line = args.Line, Column = args.Column };
            var graph = await _analyzer.BuildCallGraphAsync(query, args.MaxNodes, args.Depth ?? 1, cancellationToken);
            var payload = new { schema = "codepunk.roslyn.callgraph.v1", root = graph.Root, outbound = graph.Calls, inbound = graph.Callers, edges = graph.Edges, truncated = graph.Truncated };
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            return new ToolResult { Content = json };
        }
        catch (OperationCanceledException)
        { return new ToolResult { Content = "Operation cancelled", IsError = true, ErrorMessage = "Cancelled" }; }
        catch (Exception ex)
        { return new ToolResult { Content = $"Roslyn error: {ex.Message}", IsError = true, ErrorMessage = ex.Message }; }
    }
}
