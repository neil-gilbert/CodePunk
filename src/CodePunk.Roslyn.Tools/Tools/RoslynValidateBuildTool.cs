using System.Text.Json;
using CodePunk.Core.Services;
using CodePunk.Roslyn.Abstractions;

namespace CodePunk.Roslyn.Tools;

public sealed class RoslynValidateBuildTool : ITool
{
    private readonly IRoslynWorkspaceService _workspace;

    public RoslynValidateBuildTool(IRoslynWorkspaceService workspace)
    { _workspace = workspace; }

    public string Name => "roslyn_validate_build";
    public string Description => "Compile all projects and return grouped compile errors (no apply).";
    public JsonElement Parameters => JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}} ").RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            await _workspace.InitializeAsync(null, cancellationToken);
            var solution = await _workspace.GetSolutionAsync(cancellationToken);
            var results = new List<object>();
            int totalErrors = 0;
            foreach (var project in solution.Projects)
            {
                var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
                if (compilation == null) continue;
                var diags = compilation.GetDiagnostics(cancellationToken).Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
                totalErrors += diags.Count;
                var items = diags.Select(d =>
                {
                    var span = d.Location.GetLineSpan();
                    return new {
                        id = d.Id,
                        message = d.GetMessage(),
                        file = span.Path,
                        line = span.StartLinePosition.Line + 1,
                        column = span.StartLinePosition.Character + 1
                    };
                }).ToList();
                results.Add(new { project = project.Name, errors = items.Count, items });
            }
            var payload = new { schema = "codepunk.roslyn.build.v1", totalErrors, projects = results };
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            return new ToolResult { Content = json };
        }
        catch (OperationCanceledException)
        { return new ToolResult { Content = "Operation cancelled", IsError = true, ErrorMessage = "Cancelled" }; }
        catch (Exception ex)
        { return new ToolResult { Content = $"Roslyn error: {ex.Message}", IsError = true, ErrorMessage = ex.Message }; }
    }
}

