using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using CodePunk.Core.Services;
using CodePunk.Core.Utils;
using CodePunk.Roslyn.Abstractions;
using CodePunk.Roslyn.Models;

namespace CodePunk.Roslyn.Tools;

public sealed class AnalyzeArgs
{
    [Display(Description = "Optional path to .sln or .csproj. Defaults to auto-discovery from CWD.")]
    public string? SolutionPath { get; set; }

    [Display(Description = "Maximum diagnostics to return (default 200)")]
    public int? MaxItems { get; set; }
}

public sealed class RoslynAnalyzeTool : ITool
{
    private readonly IRoslynAnalyzerService _analyzer;
    private readonly IRoslynWorkspaceService _workspace;

    public RoslynAnalyzeTool(IRoslynAnalyzerService analyzer, IRoslynWorkspaceService workspace)
    {
        _analyzer = analyzer;
        _workspace = workspace;
    }

    public string Name => "roslyn_analyze";
    public string Description => "Analyze a C# solution/project and return a compact diagnostics summary.";
    public JsonElement Parameters => JsonSchemaGenerator.Generate<AnalyzeArgs>();

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (!ToolArgumentBinder.TryBindAndValidate<AnalyzeArgs>(arguments, out var args, out var error))
        {
            return new ToolResult { Content = error ?? "Invalid arguments", IsError = true, ErrorMessage = error };
        }

        try
        {
            await _workspace.InitializeAsync(args!.SolutionPath, cancellationToken);
            var res = await _analyzer.AnalyzeAsync(new RoslynAnalyzeOptions
            {
                SolutionPath = args.SolutionPath,
                IncludeDiagnostics = true,
                MaxItems = args.MaxItems ?? 200
            }, cancellationToken);

            var payload = new
            {
                schema = "codepunk.roslyn.diagnostics.v1",
                summary = new { errors = res.ErrorCount, warnings = res.WarningCount, infos = res.InfoCount },
                items = res.Items
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

