using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using CodePunk.Core.Services;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Utils;
using CodePunk.Roslyn.Abstractions;
using CodePunk.Roslyn.Models;

namespace CodePunk.Roslyn.Tools;

public sealed class RefactorArgs
{
    [Display(Description = "Operation to perform (supported: rename)")]
    [Required]
    public string? Operation { get; set; }

    [Display(Description = "Fully-qualified symbol name (e.g., Namespace.Type.Member). Optional if using location.")]
    public string? FullyQualifiedName { get; set; }

    [Display(Description = "Source file path for location-based lookup.")]
    public string? File { get; set; }

    [Display(Description = "1-based line number for location-based lookup.")]
    public int? Line { get; set; }

    [Display(Description = "1-based column number for location-based lookup.")]
    public int? Column { get; set; }

    [Display(Description = "New name for rename operation")]
    public string? NewName { get; set; }

    [Display(Description = "Diagnostic ID or fix ID for apply-fix operation (e.g., CS8019 or remove-unused-usings)")]
    public string? DiagnosticId { get; set; }
}

public sealed class RoslynRefactorTool : ITool
{
    private readonly IRoslynRefactorService _refactor;
    private readonly IRoslynWorkspaceService _workspace;
    private readonly IDiffService _diffs;

    public RoslynRefactorTool(IRoslynRefactorService refactor, IRoslynWorkspaceService workspace, IDiffService diffs)
    {
        _refactor = refactor; _workspace = workspace; _diffs = diffs;
    }

    public string Name => "roslyn_refactor";
    public string Description => "Apply Roslyn-backed refactors (rename). Returns changed files and diffs; does not apply.";
    public JsonElement Parameters => JsonSchemaGenerator.Generate<RefactorArgs>();

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (!ToolArgumentBinder.TryBindAndValidate<RefactorArgs>(arguments, out var args, out var error))
        { return new ToolResult { Content = error ?? "Invalid arguments", IsError = true, ErrorMessage = error }; }

        try
        {
            await _workspace.InitializeAsync(null, cancellationToken);
            RoslynEditBatch batch;
            if (string.Equals(args!.Operation, "rename", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(args.NewName))
                {
                    return new ToolResult { Content = "NewName is required for rename", IsError = true, ErrorMessage = "InvalidArguments" };
                }
                var renameArgs = new RoslynRenameArgs
                {
                    NewName = args.NewName!,
                    Target = new RoslynSymbolQuery
                    {
                        FullyQualifiedName = args.FullyQualifiedName,
                        FilePath = args.File,
                        Line = args.Line,
                        Column = args.Column
                    }
                };
                batch = await _refactor.RenameSymbolAsync(renameArgs, cancellationToken);
            }
            else if (string.Equals(args.Operation, "apply-fix", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(args.DiagnosticId))
                {
                    return new ToolResult { Content = "DiagnosticId is required for apply-fix", IsError = true, ErrorMessage = "InvalidArguments" };
                }
                var fixArgs = new RoslynCodeFixArgs { DiagnosticIdOrFixId = args.DiagnosticId!, FilePath = args.File };
                batch = await _refactor.ApplyCodeFixAsync(fixArgs, cancellationToken);
            }
            else
            {
                return new ToolResult { Content = "Unsupported operation. Use 'rename' or 'apply-fix'", IsError = true, ErrorMessage = "UnsupportedOperation" };
            }
            // Compute diffs vs disk
            var files = new List<object?>();
            foreach (var e in batch.Edits)
            {
                var path = e.Path;
                string beforeDisk;
                try { beforeDisk = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : e.Before; } catch { beforeDisk = e.Before; }
                var diff = _diffs.CreateUnifiedDiff(path, beforeDisk.Replace("\r\n","\n"), e.After.Replace("\r\n","\n"));
                files.Add(new { path, diff });
            }
            var payload = new { schema = "codepunk.roslyn.edits.v1", operation = args.Operation, files };
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            return new ToolResult { Content = json };
        }
        catch (OperationCanceledException)
        { return new ToolResult { Content = "Operation cancelled", IsError = true, ErrorMessage = "Cancelled" }; }
        catch (Exception ex)
        { return new ToolResult { Content = $"Roslyn error: {ex.Message}", IsError = true, ErrorMessage = ex.Message }; }
    }
}
