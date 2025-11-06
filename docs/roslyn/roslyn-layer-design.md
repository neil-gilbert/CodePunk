# CodePunk Roslyn Layer – Technical Design

## Overview

Add a Roslyn-powered layer that loads .NET solutions/projects and exposes semantic analysis, diagnostics, refactoring, and code-generation to CodePunk’s tool system and plan workflow. The layer integrates without breaking existing non-C# workflows and preserves CodePunk’s plan → diff → apply pattern by producing text diffs from AST-backed changes.

## Goals

- Load .sln/.csproj using MSBuild/Workspace APIs.
- Provide semantic model data (symbols, references, call graphs, syntax trees, diagnostics).
- Offer CLI and LLM-callable tools:
  - `roslyn analyze` (diagnostics + stats)
  - `roslyn explain-symbol` (symbol resolution + explanation + references)
  - `roslyn refactor` (rename, apply code fix, extract method, etc.)
  - `roslyn codegen` (LLM-assisted generation validated by Roslyn)
- Integrate with CodePunk tool registry and chat tool-calling.
- Preserve plan staging: Roslyn refactors produce updated file contents and unified diffs; apply remains text-based and drift-safe.
- Cross-language fallback remains unchanged.

## Out of Scope (for initial phase)

- Full-blown code-fix provider discovery/MEF catalogs. Start with curated/reflection-based providers and common refactor operations.
- IDE-like incremental compilation; target batch operations.
- Deep project system mutation (PackageReference add/remove) beyond simple file edits.

---

## Architecture

### New Projects

- `CodePunk.Roslyn.Core` (class library)
  - Workspace management, semantic services, DTOs, analyzers/refactor orchestrators, plan mapping.
- `CodePunk.Roslyn.Tools` (class library)
  - ITool implementations that call the core services.
  - Optional: can live inside `CodePunk.Roslyn.Core` initially and be split later.

Both projects target `net9.0` and reference Roslyn packages.

### Key Services and Interfaces

- `IRoslynWorkspaceService`
  - Loads and caches a `Solution` using `MSBuildWorkspace`.
  - Resolves `Project`, `Document`, `SemanticModel` and maps file paths to `DocumentId`.
  - File watchers to invalidate caches on .sln/.csproj changes.
  - Methods:
    - `Task InitializeAsync(string? slnOrProjectPath, CancellationToken)`
    - `Task<Solution> GetSolutionAsync(CancellationToken)`
    - `Task<(Document? Document, SemanticModel? Model)> GetDocumentModelAsync(string path, CancellationToken)`
    - `Task<ISymbol?> FindSymbolAsync(RoslynSymbolQuery query, CancellationToken)`
- `IRoslynAnalyzerService`
  - Fetch diagnostics across solution, per project, or per document.
  - Simple call graph creation using `ISymbol` relationships.
  - Methods:
    - `Task<RoslynDiagnosticsResult> AnalyzeAsync(RoslynAnalyzeOptions, CancellationToken)`
    - `Task<RoslynSymbolInfo> ExplainSymbolAsync(RoslynSymbolQuery, CancellationToken)`
- `IRoslynRefactorService`
  - High-level refactors (rename, apply code fix ID, extract method) and returns changed documents.
  - Methods:
    - `Task<RoslynEditBatch> RenameSymbolAsync(RoslynRenameArgs, CancellationToken)`
    - `Task<RoslynEditBatch> ApplyCodeFixAsync(RoslynCodeFixArgs, CancellationToken)`
    - `Task<RoslynEditBatch> ApplyCustomEditsAsync(Func<Solution, Task<Solution>>, CancellationToken)`
- `IRoslynPlanBuilder`
  - Convert a `RoslynEditBatch` (changed documents) into CodePunk plan entries by computing text diffs vs current files.
  - Methods:
    - `Task<IReadOnlyList<PlanFileChange>> BuildPlanAsync(RoslynEditBatch, CancellationToken)`
- `IRoslynContextBuilder`
  - Emit compact JSON(ish) snapshots for LLM consumption (symbols, trees, refs, diagnostics) with size guards.
  - Methods:
    - `RoslynContext BuildForDocument(Document doc, SemanticModel model, RoslynContextOptions opts)`
    - `RoslynContext BuildForSymbol(ISymbol symbol, RoslynContextOptions opts)`

### Core DTOs (shape)

- `RoslynAnalyzeOptions { string? PathOrProject; string? Include; bool IncludeDiagnostics; bool IncludeReferences; int MaxItems }`
- `RoslynSymbolQuery { string? FullyQualifiedName; string? FilePath; int? Line; int? Column; }`
- `RoslynRenameArgs { RoslynSymbolQuery Target; string NewName; bool PreviewOnly; }`
- `RoslynCodeFixArgs { string DiagnosticIdOrFixId; RoslynSymbolQuery? Scope; }`
- `RoslynEditBatch { IReadOnlyList<RoslynEdit> Edits; }` where `RoslynEdit { string Path; string Before; string After; }`
- `RoslynDiagnosticsResult { int Errors; int Warnings; List<RoslynDiagnostic> Items; }`
- `RoslynSymbolInfo { string Name; string Kind; string? ContainingType; string? ContainingNamespace; List<string> Locations; List<string> References; string? SummaryXml; string? Signature; }`
- `RoslynContext { JsonElement Data; int ApproxBytes; bool Truncated; }`

### Workspace Loading

- Use `Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults()` once (lazy, thread-safe).
- Use `MSBuildWorkspace.Create()` to open solution/project. If path not provided, auto-discover `.sln` in CWD, else first `.csproj`.
- Cache `Solution` and `Project` by absolute path; monitor file changes to invalidate.
- Resolve a file path to a `Document` via `workspace.CurrentSolution.Projects.SelectMany(p => p.Documents)` mapping.
- Respect bin/obj/.generated files ignore list to reduce noise.

### Exposing Roslyn Results to LLM

- Tools return compact, schema-tagged JSON as `ToolResult.Content`.
- Default schemas (string identifiers only, no heavy inline metadata):
  - `codepunk.roslyn.diagnostics.v1`
  - `codepunk.roslyn.symbol.v1`
  - `codepunk.roslyn.callgraph.v1`
- `IRoslynContextBuilder` truncates large fields, annotates with `{ truncated: true }` markers.
- Example return content for explain-symbol:
```
{ "schema": "codepunk.roslyn.symbol.v1",
  "symbol": {
    "name": "MyType.DoWork",
    "kind": "Method",
    "signature": "void DoWork(int x)",
    "containingNamespace": "Acme.Core",
    "locations": ["src/Core/MyType.cs:42:13"],
    "references": ["src/App/Program.cs:15:21"],
    "summaryXml": "<summary>...</summary>"
  }
}
```

### Plan Integration (AST-backed edits, text apply)

- Roslyn refactors produce a `RoslynEditBatch` with changed documents.
- `IRoslynPlanBuilder` loads current on-disk content (not from workspace) → computes unified diffs via existing `IDiffService` → creates plan entries with `AfterContent` set to Roslyn-produced text.
- Users review with existing `plan diff` and apply with `plan apply` (drift-safe). No change required to apply pipeline.

---

## CLI and Tools

### CLI Commands (new module)

- `codepunk roslyn analyze [--path <file|project|sln>] [--json] [--max <n>] [--include <glob>]`
- `codepunk roslyn explain-symbol [--symbol <FQN> | --file <path> --line <n> --column <n>] [--json]`
- `codepunk roslyn refactor rename --symbol <FQN|location> --new-name <name> [--plan <planId>] [--preview]`
- `codepunk roslyn refactor apply-fix --id <DiagnosticId|FixId> [--scope <file|project>] [--plan <planId>]`

Implementation sketch: `RoslynCommandModule` adds `roslyn` root with subcommands and uses services.

### LLM Tools (ITool implementations)

- `roslyn_analyze` → returns `codepunk.roslyn.diagnostics.v1`
- `roslyn_explain_symbol` → returns `codepunk.roslyn.symbol.v1`
- `roslyn_refactor` → returns plan summary + per-file diffs; writes entries via `IPlanFileStore` when requested.

Parameters are defined via the existing `JsonSchemaGenerator` from DTOs with `[Display(Description=...)]`.

---

## DI Registration and Configuration

### Registration (Infrastructure)

In `src/CodePunk.Infrastructure/Configuration/ServiceCollectionExtensions.cs`, register Roslyn services and tools:

```csharp
// Roslyn services
services.AddSingleton<IRoslynWorkspaceService, RoslynWorkspaceService>();
services.AddScoped<IRoslynAnalyzerService, RoslynAnalyzerService>();
services.AddScoped<IRoslynRefactorService, RoslynRefactorService>();
services.AddScoped<IRoslynPlanBuilder, RoslynPlanBuilder>();
services.AddSingleton<IRoslynContextBuilder, RoslynContextBuilder>();

// Tools
services.AddScoped<ITool, RoslynAnalyzeTool>();
services.AddScoped<ITool, RoslynExplainSymbolTool>();
services.AddScoped<ITool, RoslynRefactorTool>();
```

Optionally add a `Roslyn` section to configuration:

```json
{
  "Roslyn": {
    "SolutionPath": null, // optional override; default: auto-discover
    "MaxContextBytes": 50000,
    "EnableFileWatch": true
  }
}
```

### NuGet Dependencies

- `Microsoft.CodeAnalysis` (Roslyn)
- `Microsoft.CodeAnalysis.CSharp.Workspaces`
- `Microsoft.CodeAnalysis.Workspaces.MSBuild`
- `Microsoft.Build.Locator`
- Optional for transforms (simplify/format): `Microsoft.CodeAnalysis.Features`

Initialize `MSBuildLocator.RegisterDefaults()` once (static ctor in `RoslynWorkspaceService`).

Example project file (CodePunk.Roslyn.Core.csproj):

```
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis" Version="4.*" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="4.*" />
    <PackageReference Include="Microsoft.CodeAnalysis.MSBuild" Version="4.*" />
    <PackageReference Include="Microsoft.Build.Locator" Version="1.*" />
    <PackageReference Include="Microsoft.CodeAnalysis.Features" Version="4.*" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\CodePunk.Core\CodePunk.Core.csproj" />
  </ItemGroup>
</Project>
```

---

## Backward Compatibility and Multi-language

- Existing tools unchanged; non-C# files continue to use text-based operations.
- Roslyn tools are additive and self-describing in the LLM tool list.
- Plan staging/apply formats remain identical; only the source of `AfterContent` differs.
- If no .sln/.csproj found or MSBuild unavailable, Roslyn tools report a clear error and suggest fallback.

---

## Errors, Limits, and Performance

- Guardrails:
  - Cap serialized contexts via `MaxContextBytes`.
  - Limit diagnostics and reference counts via `MaxItems`.
  - Timeouts on analysis and refactor operations with cancellation support.
- Cache solution load and symbol maps; invalidate on .sln/.csproj timestamp changes.
- Avoid loading generated/binary docs; skip `bin/`, `obj/`, `.g.cs` by default.

---

## Test Plan

Unit tests (new test project `CodePunk.Roslyn.Tests`):

- Workspace
  - Auto-discovery of solution from CWD.
  - Open single `*.csproj` when no `*.sln` present.
  - Map `string path → Document` and get `SemanticModel`.
- Analyzer
  - Return diagnostics for a known file (inject a small sample project). Validate counts and top N payload truncation.
- Symbol
  - Resolve symbol by FQN and by (file, line, column). Validate `RoslynSymbolInfo` fields.
- Refactor
  - Rename symbol and confirm updated text for affected documents; ensure `RoslynEditBatch` enumerates unique file changes.
- Plan Builder
  - Convert edits into `PlanFileChange` with diffs using existing `IDiffService`.
- Tool Wiring
  - Ensure `IToolService.GetLLMTools()` includes roslyn tools with correct compact descriptions and JSON schemas.

Integration tests:

- Run `codepunk roslyn analyze --json` against a small sample solution (e.g., `TestMessageCount/`).
- `roslyn refactor rename` generates a plan; verify `plan diff` non-empty; `plan apply --dry-run` summary.

---

## Example: High-level Flow (CLI → LLM → Roslyn → Plan)

1) User runs: `codepunk chat` and asks the assistant to “rename method X to Y across the solution”.
2) LLM chooses tool `roslyn_refactor` with args `{ action: "rename", symbol: { file, line, column }, newName: "Y" }`.
3) `IToolService` dispatches to `RoslynRefactorTool`, which calls `IRoslynRefactorService.RenameSymbolAsync(...)`.
4) Service computes new `Solution`, produces `RoslynEditBatch` with changed docs.
5) `IRoslynPlanBuilder` converts edits to plan entries (`AfterContent`) and persists via `IPlanFileStore`.
6) Tool returns JSON with `{ schema: "codepunk.roslyn.plan.v1", planId, filesChanged, diagnostics }`.
7) User reviews with `codepunk plan diff --id <planId>` and applies with `codepunk plan apply --id <planId>` (drift-safe).

---

## Open Questions / Future Work

- Code-fix provider discovery (MEF) versus explicit registry.
- Cross-file code generation guardrails (type introduction, `partial` usage) and solution-wide build validation.
- Background solution build hooks to surface MSBuild diagnostics.
