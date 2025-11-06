using System.CommandLine;
using System.Linq;
using System.IO;
using Spectre.Console;
using Microsoft.Extensions.DependencyInjection;
using CodePunk.Roslyn.Abstractions;
using CodePunk.Roslyn.Models;

namespace CodePunk.Console.Commands.Modules;

internal sealed class RoslynCommandModule : ICommandModule
{
    public void Register(RootCommand root, IServiceProvider services)
    {
        var roslyn = new Command("roslyn", "Roslyn-powered C# analysis and refactoring");
        BuildAnalyze(roslyn, services);
        BuildExplain(roslyn, services);
        BuildRefactor(roslyn, services);
        BuildCallGraph(roslyn, services);
        BuildValidate(roslyn, services);
        root.AddCommand(roslyn);
    }

    private static void BuildAnalyze(Command parent, IServiceProvider services)
    {
        var cmd = new Command("analyze", "Analyze solution/project diagnostics");
        var pathOpt = new Option<string>("--path", () => string.Empty, "Path to .sln or .csproj (optional)");
        var jsonOpt = new Option<bool>("--json", description: "Emit JSON");
        var maxOpt = new Option<int>("--max", () => 200, "Maximum diagnostics to include in output");
        cmd.AddOption(pathOpt); cmd.AddOption(jsonOpt); cmd.AddOption(maxOpt);
        cmd.SetHandler(async (System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            var solutionPath = ctx.ParseResult.GetValueForOption(pathOpt);
            var json = ctx.ParseResult.GetValueForOption(jsonOpt);
            var max = ctx.ParseResult.GetValueForOption(maxOpt);
            var console = services.GetRequiredService<IAnsiConsole>();
            var workspace = services.GetRequiredService<IRoslynWorkspaceService>();
            var analyzer = services.GetRequiredService<IRoslynAnalyzerService>();
            try
            {
                await workspace.InitializeAsync(string.IsNullOrWhiteSpace(solutionPath) ? null : solutionPath);
                var res = await analyzer.AnalyzeAsync(new RoslynAnalyzeOptions { SolutionPath = solutionPath, MaxItems = max });
                if (json)
                {
                    var payload = new { schema = "codepunk.roslyn.diagnostics.v1", summary = new { errors = res.ErrorCount, warnings = res.WarningCount, infos = res.InfoCount }, items = res.Items };
                    var txt = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    console.WriteLine(txt);
                }
                else
                {
                    console.MarkupLine($"[bold]Diagnostics[/]  Errors: [red]{res.ErrorCount}[/], Warnings: [yellow]{res.WarningCount}[/], Info: [blue]{res.InfoCount}[/]");
                    foreach (var d in res.Items.Take(20))
                    {
                        var loc = string.IsNullOrEmpty(d.File) ? "" : $" {d.File}:{d.Line}:{d.Column}";
                        console.MarkupLine($"[dim]{d.Id}[/] [italic]{d.Severity}[/]{loc} - {Markup.Escape(d.Message)}");
                    }
                    if (res.Items.Count > 20) console.MarkupLine($"[dim]... {res.Items.Count - 20} more ...[/]");
                }
                ctx.ExitCode = 0;
            }
            catch (Exception ex)
            {
                console.MarkupLine($"[red]Roslyn analyze error:[/] {Markup.Escape(ex.Message)}");
                ctx.ExitCode = 1;
            }
        });
        parent.AddCommand(cmd);
    }

    private static void BuildValidate(Command parent, IServiceProvider services)
    {
        var cmd = new Command("validate-build", "Compile projects and report grouped errors");
        var jsonOpt = new Option<bool>("--json", description: "Emit JSON");
        cmd.AddOption(jsonOpt);
        cmd.SetHandler(async (System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            var json = ctx.ParseResult.GetValueForOption(jsonOpt);
            var console = services.GetRequiredService<IAnsiConsole>();
            var workspace = services.GetRequiredService<IRoslynWorkspaceService>();
            try
            {
                await workspace.InitializeAsync(null);
                var solution = await workspace.GetSolutionAsync();
                var results = new List<object>();
                int total = 0;
                foreach (var project in solution.Projects)
                {
                    var comp = await project.GetCompilationAsync();
                    if (comp == null) continue;
                    var errs = comp.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
                    total += errs.Count;
                    var items = errs.Take(50).Select(d =>
                    {
                        var span = d.Location.GetLineSpan();
                        return new { id = d.Id, file = span.Path, line = span.StartLinePosition.Line + 1, column = span.StartLinePosition.Character + 1, message = d.GetMessage() };
                    }).ToList();
                    results.Add(new { project = project.Name, errors = errs.Count, sample = items });
                }
                if (json)
                {
                    var payload = new { schema = "codepunk.roslyn.build.v1", totalErrors = total, projects = results };
                    var txt = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    console.WriteLine(txt);
                }
                else
                {
                    if (total == 0)
                    {
                        console.MarkupLine("[green]Build OK[/]: no compile errors found.");
                    }
                    else
                    {
                        console.MarkupLine($"[red]Build errors[/]: {total}");
                        foreach (dynamic pr in results)
                        {
                            console.MarkupLine($"- {pr.project}: {pr.errors}");
                        }
                    }
                }
                ctx.ExitCode = 0;
            }
            catch (Exception ex)
            {
                console.MarkupLine($"[red]Roslyn validate-build error:[/] {Markup.Escape(ex.Message)}");
                ctx.ExitCode = 1;
            }
        });
        parent.AddCommand(cmd);
    }

    private static void BuildRefactor(Command parent, IServiceProvider services)
    {
        var refactor = new Command("refactor", "Apply Roslyn refactors");
        var rename = new Command("rename", "Rename a symbol and stage changes to a plan (optional)");
        var fqnOpt = new Option<string>("--symbol", () => string.Empty, "Fully-qualified symbol name");
        var fileOpt = new Option<string>("--file", () => string.Empty, "File for location-based lookup");
        var lineOpt = new Option<int>("--line", () => 0, "1-based line");
        var colOpt = new Option<int>("--column", () => 0, "1-based column");
        var newNameOpt = new Option<string>("--new-name", description: "New symbol name") { IsRequired = true };
        var planOpt = new Option<string>("--plan", () => string.Empty, "Existing plan id to add changes to (optional)");
        var goalOpt = new Option<string>("--goal", () => string.Empty, "Plan goal if creating a new plan");
        var jsonOpt = new Option<bool>("--json", description: "Emit JSON payload");
        rename.AddOption(fqnOpt); rename.AddOption(fileOpt); rename.AddOption(lineOpt); rename.AddOption(colOpt);
        rename.AddOption(newNameOpt); rename.AddOption(planOpt); rename.AddOption(goalOpt); rename.AddOption(jsonOpt);
        rename.SetHandler(async (System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            var fqn = ctx.ParseResult.GetValueForOption(fqnOpt);
            var file = ctx.ParseResult.GetValueForOption(fileOpt);
            var line = ctx.ParseResult.GetValueForOption(lineOpt);
            var col = ctx.ParseResult.GetValueForOption(colOpt);
            var newName = ctx.ParseResult.GetValueForOption(newNameOpt);
            var planId = ctx.ParseResult.GetValueForOption(planOpt);
            var goal = ctx.ParseResult.GetValueForOption(goalOpt);
            var json = ctx.ParseResult.GetValueForOption(jsonOpt);
            var console = services.GetRequiredService<IAnsiConsole>();
            var workspace = services.GetRequiredService<IRoslynWorkspaceService>();
            var refactorSvc = services.GetRequiredService<IRoslynRefactorService>();
            var planStore = services.GetRequiredService<CodePunk.Console.Stores.IPlanFileStore>();
            var diffs = services.GetRequiredService<CodePunk.Core.Abstractions.IDiffService>();
            try
            {
                await workspace.InitializeAsync(null);
                var query = new CodePunk.Roslyn.Models.RoslynSymbolQuery
                {
                    FullyQualifiedName = string.IsNullOrWhiteSpace(fqn) ? null : fqn,
                    FilePath = string.IsNullOrWhiteSpace(file) ? null : file,
                    Line = line > 0 ? line : null,
                    Column = col > 0 ? col : null
                };
                var batch = await refactorSvc.RenameSymbolAsync(new CodePunk.Roslyn.Models.RoslynRenameArgs { Target = query, NewName = newName });

                // Stage to plan if requested (or create new plan)
                string? stagedPlanId = null;
                int added = 0; int updated = 0;
                if (!string.IsNullOrWhiteSpace(planId) || batch.Edits.Count > 0)
                {
                    stagedPlanId = string.IsNullOrWhiteSpace(planId) ? await planStore.CreateAsync(string.IsNullOrWhiteSpace(goal) ? $"Refactor: rename → {newName}" : goal) : planId;
                    var record = await planStore.GetAsync(stagedPlanId!);
                    if (record == null) throw new InvalidOperationException("Plan not found");

                    foreach (var e in batch.Edits)
                    {
                        var relPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), e.Path);
                        string beforeDisk = string.Empty;
                        if (File.Exists(relPath))
                        {
                            try { beforeDisk = await File.ReadAllTextAsync(relPath); } catch { beforeDisk = e.Before; }
                        }
                        var diff = diffs.CreateUnifiedDiff(relPath, (beforeDisk ?? string.Empty).Replace("\r\n","\n"), e.After.Replace("\r\n","\n"));
                        var beforeHash = CodePunk.Console.Stores.PlanFileStore.Sha256(beforeDisk ?? string.Empty);
                        var afterHash = CodePunk.Console.Stores.PlanFileStore.Sha256(e.After);
                        var existing = record.Files.FirstOrDefault(f => string.Equals(f.Path, relPath, StringComparison.OrdinalIgnoreCase));
                        if (existing == null)
                        {
                            record.Files.Add(new CodePunk.Console.Stores.PlanFileChange {
                                Path = relPath,
                                HashBefore = beforeHash,
                                HashAfter = afterHash,
                                Diff = diff,
                                BeforeContent = beforeDisk,
                                AfterContent = e.After,
                                Rationale = $"Roslyn rename to {newName}",
                                Generated = false,
                                IsDelete = false
                            });
                            added++;
                        }
                        else
                        {
                            existing.HashBefore = beforeHash;
                            existing.HashAfter = afterHash;
                            existing.Diff = diff;
                            existing.BeforeContent = beforeDisk;
                            existing.AfterContent = e.After;
                            existing.Rationale = $"Roslyn rename to {newName}";
                            existing.IsDelete = false;
                            existing.Generated ??= false;
                            updated++;
                        }
                    }
                    await planStore.SaveAsync(record);
                }

                if (json)
                {
                    var payload = new {
                        schema = "codepunk.roslyn.plan.v1",
                        operation = "rename",
                        planId = stagedPlanId,
                        files = batch.Edits.Select(e => new { path = Path.GetRelativePath(Directory.GetCurrentDirectory(), e.Path) }).ToList(),
                        added,
                        updated
                    };
                    var txt = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    console.WriteLine(txt);
                }
                else
                {
                    if (stagedPlanId != null)
                    {
                        console.MarkupLine($"Staged [green]{batch.Edits.Count}[/] file(s) to plan [bold]{stagedPlanId}[/]. Added: {added}, Updated: {updated}.");
                    }
                    else
                    {
                        console.MarkupLine($"No plan specified. Computed {batch.Edits.Count} edit(s) (not staged). Use --plan to persist.");
                    }
                }
                ctx.ExitCode = 0;
            }
            catch (Exception ex)
            {
                console.MarkupLine($"[red]Roslyn refactor error:[/] {Markup.Escape(ex.Message)}");
                ctx.ExitCode = 1;
            }
        });
        refactor.AddCommand(rename);

        var applyFix = new Command("apply-fix", "Apply a curated Roslyn fix/transform (format, simplify-names, remove-unused-usings, sort-usings)");
        var idOpt = new Option<string>("--id", description: "ID/alias: format | simplify-names | remove-unused-usings | sort-usings | organize-usings | IDE0001 | CS8019 | IDE0005") { IsRequired = true };
        var fileOpt2 = new Option<string>("--file", () => string.Empty, "Optional file to restrict fix to");
        var planOpt2 = new Option<string>("--plan", () => string.Empty, "Existing plan id to add changes to (optional)");
        var goalOpt2 = new Option<string>("--goal", () => string.Empty, "Plan goal if creating a new plan");
        var jsonOpt2 = new Option<bool>("--json", description: "Emit JSON payload");
        applyFix.AddOption(idOpt); applyFix.AddOption(fileOpt2); applyFix.AddOption(planOpt2); applyFix.AddOption(goalOpt2); applyFix.AddOption(jsonOpt2);
        applyFix.SetHandler(async (System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            var id = ctx.ParseResult.GetValueForOption(idOpt);
            var file = ctx.ParseResult.GetValueForOption(fileOpt2);
            var planId = ctx.ParseResult.GetValueForOption(planOpt2);
            var goal = ctx.ParseResult.GetValueForOption(goalOpt2);
            var json = ctx.ParseResult.GetValueForOption(jsonOpt2);
            var console = services.GetRequiredService<IAnsiConsole>();
            var workspace = services.GetRequiredService<IRoslynWorkspaceService>();
            var refactorSvc = services.GetRequiredService<IRoslynRefactorService>();
            var planStore = services.GetRequiredService<CodePunk.Console.Stores.IPlanFileStore>();
            var diffs = services.GetRequiredService<CodePunk.Core.Abstractions.IDiffService>();
            try
            {
                await workspace.InitializeAsync(null);
                var batch = await refactorSvc.ApplyCodeFixAsync(new CodePunk.Roslyn.Models.RoslynCodeFixArgs { DiagnosticIdOrFixId = id, FilePath = string.IsNullOrWhiteSpace(file) ? null : file });
                string? stagedPlanId = null; int added = 0, updated = 0;
                if (!string.IsNullOrWhiteSpace(planId) || batch.Edits.Count > 0)
                {
                    stagedPlanId = string.IsNullOrWhiteSpace(planId) ? await planStore.CreateAsync(string.IsNullOrWhiteSpace(goal) ? $"Apply fix {id}" : goal) : planId;
                    var record = await planStore.GetAsync(stagedPlanId!);
                    if (record == null) throw new InvalidOperationException("Plan not found");
                    foreach (var e in batch.Edits)
                    {
                        var relPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), e.Path);
                        string beforeDisk = string.Empty;
                        if (File.Exists(relPath))
                        {
                            try { beforeDisk = await File.ReadAllTextAsync(relPath); } catch { beforeDisk = e.Before; }
                        }
                        var diff = diffs.CreateUnifiedDiff(relPath, (beforeDisk ?? string.Empty).Replace("\r\n","\n"), e.After.Replace("\r\n","\n"));
                        var beforeHash = CodePunk.Console.Stores.PlanFileStore.Sha256(beforeDisk ?? string.Empty);
                        var afterHash = CodePunk.Console.Stores.PlanFileStore.Sha256(e.After);
                        var existing = record.Files.FirstOrDefault(f => string.Equals(f.Path, relPath, StringComparison.OrdinalIgnoreCase));
                        if (existing == null)
                        {
                            record.Files.Add(new CodePunk.Console.Stores.PlanFileChange {
                                Path = relPath,
                                HashBefore = beforeHash,
                                HashAfter = afterHash,
                                Diff = diff,
                                BeforeContent = beforeDisk,
                                AfterContent = e.After,
                                Rationale = $"Roslyn apply-fix {id}",
                                Generated = false,
                                IsDelete = false
                            });
                            added++;
                        }
                        else
                        {
                            existing.HashBefore = beforeHash;
                            existing.HashAfter = afterHash;
                            existing.Diff = diff;
                            existing.BeforeContent = beforeDisk;
                            existing.AfterContent = e.After;
                            existing.Rationale = $"Roslyn apply-fix {id}";
                            existing.IsDelete = false;
                            existing.Generated ??= false;
                            updated++;
                        }
                    }
                    await planStore.SaveAsync(record);
                }

                if (json)
                {
                    var payload = new { schema = "codepunk.roslyn.plan.v1", operation = "apply-fix", fix = id, planId = stagedPlanId, files = batch.Edits.Select(e => new { path = Path.GetRelativePath(Directory.GetCurrentDirectory(), e.Path) }), added, updated };
                    var txt = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    console.WriteLine(txt);
                }
                else
                {
                    if (stagedPlanId != null)
                        console.MarkupLine($"Staged [green]{batch.Edits.Count}[/] file(s) to plan [bold]{stagedPlanId}[/] for fix [italic]{id}[/]. Added: {added}, Updated: {updated}.");
                    else
                        console.MarkupLine($"Computed {batch.Edits.Count} edit(s) for fix [italic]{id}[/] (not staged). Use --plan to persist.");
                }
                ctx.ExitCode = 0;
            }
            catch (Exception ex)
            {
                console.MarkupLine($"[red]Roslyn apply-fix error:[/] {Markup.Escape(ex.Message)}");
                ctx.ExitCode = 1;
            }
        });
        refactor.AddCommand(applyFix);
        parent.AddCommand(refactor);
    }

    private static void BuildCallGraph(Command parent, IServiceProvider services)
    {
        var cmd = new Command("call-graph", "Build a call graph (inbound/outbound) for a method");
        var fqnOpt = new Option<string>("--symbol", () => string.Empty, "Fully-qualified symbol name");
        var fileOpt = new Option<string>("--file", () => string.Empty, "File for location-based lookup");
        var lineOpt = new Option<int>("--line", () => 0, "1-based line");
        var colOpt = new Option<int>("--column", () => 0, "1-based column");
        var maxOpt = new Option<int>("--max", () => 200, "Maximum nodes to include");
        var depthOpt = new Option<int>("--depth", () => 1, "Traversal depth (1 = direct only)");
        var jsonOpt = new Option<bool>("--json", description: "Emit JSON");
        cmd.AddOption(fqnOpt); cmd.AddOption(fileOpt); cmd.AddOption(lineOpt); cmd.AddOption(colOpt); cmd.AddOption(maxOpt); cmd.AddOption(depthOpt); cmd.AddOption(jsonOpt);
        cmd.SetHandler(async (System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            var fqn = ctx.ParseResult.GetValueForOption(fqnOpt);
            var file = ctx.ParseResult.GetValueForOption(fileOpt);
            var line = ctx.ParseResult.GetValueForOption(lineOpt);
            var col = ctx.ParseResult.GetValueForOption(colOpt);
            var max = ctx.ParseResult.GetValueForOption(maxOpt);
            var depth = ctx.ParseResult.GetValueForOption(depthOpt);
            var json = ctx.ParseResult.GetValueForOption(jsonOpt);
            var console = services.GetRequiredService<IAnsiConsole>();
            var workspace = services.GetRequiredService<IRoslynWorkspaceService>();
            var analyzer = services.GetRequiredService<IRoslynAnalyzerService>();
            try
            {
                await workspace.InitializeAsync(null);
                var query = new CodePunk.Roslyn.Models.RoslynSymbolQuery { FullyQualifiedName = string.IsNullOrWhiteSpace(fqn) ? null : fqn, FilePath = string.IsNullOrWhiteSpace(file) ? null : file, Line = line > 0 ? line : null, Column = col > 0 ? col : null };
                var graph = await analyzer.BuildCallGraphAsync(query, max, depth);
                if (json)
                {
                    var payload = new { schema = "codepunk.roslyn.callgraph.v1", root = graph.Root, outbound = graph.Calls, inbound = graph.Callers, truncated = graph.Truncated };
                    var txt = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    console.WriteLine(txt);
                }
                else
                {
                    console.MarkupLine($"[bold]Root[/]: {Markup.Escape(graph.Root.Id)} ([dim]{graph.Root.Kind}[/])");
                    if (graph.Calls.Count > 0)
                    {
                        console.MarkupLine("Outbound calls:");
                        foreach (var n in graph.Calls.Take(10)) console.MarkupLine("  - " + Markup.Escape(n.Id));
                        if (graph.Calls.Count > 10) console.MarkupLine($"  [dim]... {graph.Calls.Count - 10} more ...[/]");
                    }
                    if (graph.Callers.Count > 0)
                    {
                        console.MarkupLine("Inbound callers:");
                        foreach (var n in graph.Callers.Take(10)) console.MarkupLine("  - " + Markup.Escape(n.Id));
                        if (graph.Callers.Count > 10) console.MarkupLine($"  [dim]... {graph.Callers.Count - 10} more ...[/]");
                    }
                }
                ctx.ExitCode = 0;
            }
            catch (Exception ex)
            {
                console.MarkupLine($"[red]Roslyn call-graph error:[/] {Markup.Escape(ex.Message)}");
                ctx.ExitCode = 1;
            }
        });
        parent.AddCommand(cmd);
    }

    private static void BuildExplain(Command parent, IServiceProvider services)
    {
        var cmd = new Command("explain-symbol", "Explain a symbol by FQN or location");
        var pathOpt = new Option<string>("--path", () => string.Empty, "Path to .sln or .csproj (optional)");
        var fqnOpt = new Option<string>("--symbol", () => string.Empty, "Fully-qualified symbol name");
        var fileOpt = new Option<string>("--file", () => string.Empty, "File for location-based lookup");
        var lineOpt = new Option<int>("--line", () => 0, "1-based line");
        var colOpt = new Option<int>("--column", () => 0, "1-based column");
        var jsonOpt = new Option<bool>("--json", description: "Emit JSON");
        cmd.AddOption(pathOpt); cmd.AddOption(fqnOpt); cmd.AddOption(fileOpt); cmd.AddOption(lineOpt); cmd.AddOption(colOpt); cmd.AddOption(jsonOpt);
        cmd.SetHandler(async (System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            var solutionPath = ctx.ParseResult.GetValueForOption(pathOpt);
            var fqn = ctx.ParseResult.GetValueForOption(fqnOpt);
            var file = ctx.ParseResult.GetValueForOption(fileOpt);
            var line = ctx.ParseResult.GetValueForOption(lineOpt);
            var col = ctx.ParseResult.GetValueForOption(colOpt);
            var json = ctx.ParseResult.GetValueForOption(jsonOpt);
            var console = services.GetRequiredService<IAnsiConsole>();
            var workspace = services.GetRequiredService<IRoslynWorkspaceService>();
            var analyzer = services.GetRequiredService<IRoslynAnalyzerService>();
            try
            {
                await workspace.InitializeAsync(string.IsNullOrWhiteSpace(solutionPath) ? null : solutionPath);
                var query = new RoslynSymbolQuery
                {
                    FullyQualifiedName = string.IsNullOrWhiteSpace(fqn) ? null : fqn,
                    FilePath = string.IsNullOrWhiteSpace(file) ? null : file,
                    Line = line > 0 ? line : null,
                    Column = col > 0 ? col : null
                };
                var info = await analyzer.ExplainSymbolAsync(query, null);
                if (json)
                {
                    var payload = new { schema = "codepunk.roslyn.symbol.v1", symbol = info };
                    var txt = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    console.WriteLine(txt);
                }
                else
                {
                    console.MarkupLine($"[bold]{Markup.Escape(info.Name)}[/] ([dim]{info.Kind}[/])");
                    if (!string.IsNullOrWhiteSpace(info.Signature)) console.MarkupLine($"Signature: [italic]{Markup.Escape(info.Signature)}[/]");
                    if (!string.IsNullOrWhiteSpace(info.ContainingNamespace)) console.MarkupLine($"Namespace: [dim]{Markup.Escape(info.ContainingNamespace)}[/]");
                    if (!string.IsNullOrWhiteSpace(info.ContainingType)) console.MarkupLine($"Type: [dim]{Markup.Escape(info.ContainingType)}[/]");
                    if (info.Locations.Count > 0)
                    {
                        console.MarkupLine("Locations:");
                        foreach (var l in info.Locations.Take(5)) console.MarkupLine("  - " + Markup.Escape(l));
                        if (info.Locations.Count > 5) console.MarkupLine($"  [dim]... {info.Locations.Count - 5} more ...[/]");
                    }
                    if (info.References.Count > 0)
                    {
                        console.MarkupLine("References:");
                        foreach (var r in info.References.Take(5)) console.MarkupLine("  - " + Markup.Escape(r));
                        if (info.References.Count > 5) console.MarkupLine($"  [dim]... {info.References.Count - 5} more ...[/]");
                    }
                }
                ctx.ExitCode = 0;
            }
            catch (Exception ex)
            {
                console.MarkupLine($"[red]Roslyn explain-symbol error:[/] {Markup.Escape(ex.Message)}");
                ctx.ExitCode = 1;
            }
        });
        parent.AddCommand(cmd);
    }
}
