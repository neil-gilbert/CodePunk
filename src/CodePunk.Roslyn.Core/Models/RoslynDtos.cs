using System.ComponentModel.DataAnnotations;
using Microsoft.CodeAnalysis;

namespace CodePunk.Roslyn.Models;

public sealed class RoslynAnalyzeOptions
{
    [Display(Description = "Optional path to .sln or .csproj. Defaults to auto-discovery from CWD.")]
    public string? SolutionPath { get; set; }

    [Display(Description = "Optional glob constraint for files to include (e.g., src/**/*.cs)")]
    public string? Include { get; set; }

    [Display(Description = "Include diagnostics (default true)")]
    public bool IncludeDiagnostics { get; set; } = true;

    [Display(Description = "Maximum diagnostics to return")]
    public int MaxItems { get; set; } = 200;
}

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

public sealed class RoslynDiagnosticItem
{
    public string Id { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? File { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
}

public sealed class RoslynDiagnosticsResult
{
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int InfoCount { get; set; }
    public List<RoslynDiagnosticItem> Items { get; set; } = new();
}

public sealed class RoslynEdit
{
    public string Path { get; set; } = string.Empty;
    public string Before { get; set; } = string.Empty;
    public string After { get; set; } = string.Empty;
}

public sealed class RoslynEditBatch
{
    public IReadOnlyList<RoslynEdit> Edits { get; }
    public RoslynEditBatch(IEnumerable<RoslynEdit> edits) { Edits = edits.ToList(); }
}

public sealed class RoslynRenameArgs
{
    public RoslynSymbolQuery Target { get; set; } = new();
    [Required]
    public string NewName { get; set; } = string.Empty;
}

public sealed class RoslynCodeFixArgs
{
    [Required]
    public string DiagnosticIdOrFixId { get; set; } = string.Empty;
    public string? FilePath { get; set; }
}

public sealed class RoslynCallGraphNode
{
    public string Id { get; set; } = string.Empty; // Display string
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? Location { get; set; }
}

public sealed class RoslynCallGraphResult
{
    public RoslynCallGraphNode Root { get; set; } = new();
    public List<RoslynCallGraphNode> Calls { get; set; } = new(); // outbound
    public List<RoslynCallGraphNode> Callers { get; set; } = new(); // inbound
    public List<RoslynCallGraphEdge> Edges { get; set; } = new(); // directed edges (from -> to)
    public bool Truncated { get; set; }
}

public sealed class RoslynCallGraphEdge
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}
