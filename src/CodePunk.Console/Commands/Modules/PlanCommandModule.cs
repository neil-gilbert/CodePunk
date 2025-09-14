using System.CommandLine;
using System.Diagnostics;
using Spectre.Console;
using Microsoft.Extensions.DependencyInjection;
using CodePunk.Core.Abstractions;
using CodePunk.Console.Planning;
using CodePunk.Core.Chat;
using CodePunk.Core.Models;
using CodePunk.Console.Themes;
using CodePunk.Console.Stores;
using CodePunk.Console.Rendering;

namespace CodePunk.Console.Commands.Modules;

internal sealed class PlanCommandModule : ICommandModule
{
    public void Register(RootCommand root, IServiceProvider services)
    {
        var plan = new Command("plan", "Generate and inspect change plans");
        BuildCreate(plan, services);
        BuildAdd(plan, services);
        BuildList(plan, services);
        BuildShow(plan, services);
        BuildDiff(plan, services);
        BuildApply(plan, services);
        root.AddCommand(plan);
    }

    private static void BuildCreate(Command plan, IServiceProvider services)
    {
        var create = new Command("create", "Create a new empty plan record (AI generation TBD)");
        var goalOpt = new Option<string>("--goal");
        var fromSessionOpt = new Option<bool>("--from-session", description: "Create a plan by summarizing a session");
        var sessionOpt = new Option<string>("--session", () => string.Empty, "Session id to summarize (defaults to most recent)");
        var messagesOpt = new Option<int>("--messages", () => 20, "Maximum messages to sample from session");
        var includeToolsOpt = new Option<bool>("--include-tools", () => false, "Include tool messages in the summarization");
        var jsonOpt = new Option<bool>("--json");
        create.AddOption(goalOpt);
        create.AddOption(fromSessionOpt);
        create.AddOption(sessionOpt);
        create.AddOption(messagesOpt);
        create.AddOption(includeToolsOpt);
        create.AddOption(jsonOpt);
        create.SetHandler(async (System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            var goal = ctx.ParseResult.GetValueForOption(goalOpt);
            var fromSession = ctx.ParseResult.GetValueForOption(fromSessionOpt);
            var sessionId = ctx.ParseResult.GetValueForOption(sessionOpt);
            var messages = ctx.ParseResult.GetValueForOption(messagesOpt);
            var includeTools = ctx.ParseResult.GetValueForOption(includeToolsOpt);
            var json = ctx.ParseResult.GetValueForOption(jsonOpt);
            using var activity = Telemetry.ActivitySource.StartActivity("plan.create", ActivityKind.Client);
            var store = services.GetRequiredService<IPlanFileStore>();
            var console = services.GetRequiredService<IAnsiConsole>();

            if (fromSession)
            {
                var summarizer = services.GetService<ISessionSummarizer>();
                    if (summarizer == null)
                    {
                        if (json) { JsonOutput.Write(console, new { schema = Rendering.Schemas.PlanCreateFromSessionV1, error = new { code = "SummarizerUnavailable", message = "Session summarizer not registered" } }); return; }
                        if (!OutputContext.IsQuiet()) console.MarkupLine(ConsoleStyles.Error("Session summarizer not available"));
                        return;
                    }
                var opts = new SessionSummaryOptions { MaxMessages = messages, IncludeToolMessages = includeTools };
                var summary = await summarizer.SummarizeAsync(string.IsNullOrWhiteSpace(sessionId)? null! : sessionId, opts);
                if (summary == null)
                {
                    if (json) { JsonOutput.Write(console, new { schema = Rendering.Schemas.PlanCreateFromSessionV1, error = new { code = "SummaryUnavailable", message = "Could not infer a plan from session" } }); return; }
                    if (!OutputContext.IsQuiet()) console.MarkupLine(ConsoleStyles.Warn("Could not infer a plan from the specified session; please provide --goal"));
                    return;
                }
                var id = await store.CreateAsync(summary.Goal);
                if (json)
                {
                    var payload = new
                    {
                        schema = Rendering.Schemas.PlanCreateFromSessionV1,
                        planId = id,
                        goal = summary.Goal,
                        candidateFiles = summary.CandidateFiles,
                        source = "session",
                        messageSampleCount = summary.UsedMessages,
                        truncated = summary.Truncated
                    };
                    JsonOutput.Write(console, payload);
                    return;
                }
                if (!OutputContext.IsQuiet()) console.MarkupLine($"Created plan {ConsoleStyles.Accent(id)} from session summary");
                return;
            }

            // legacy/manual flow
            if (string.IsNullOrWhiteSpace(goal))
            {
                if (json) { JsonOutput.Write(console, new { schema = Rendering.Schemas.PlanCreateV1, error = new { code = "MissingGoal", message = "--goal is required unless --from-session is used" } }); return; }
                if (!OutputContext.IsQuiet()) console.MarkupLine(ConsoleStyles.Error("--goal is required unless --from-session is used")); return;
            }
            var newId = await store.CreateAsync(goal!);
            if (json)
            {
                var payload = new { schema = Rendering.Schemas.PlanCreateV1, planId = newId, goal };
                JsonOutput.Write(console, payload);
                return;
            }
            if (!OutputContext.IsQuiet()) console.MarkupLine($"Created plan {ConsoleStyles.Accent(newId)}");
        });
        plan.AddCommand(create);
    }

    private static void BuildAdd(Command plan, IServiceProvider services)
    {
    var add = new Command("add", "Add (stage) a file change to a plan (modify/create or delete)");
        var addIdOpt = new Option<string>("--id") { IsRequired = true };
        var addPathOpt = new Option<string>("--path") { IsRequired = true };
        var addAfterFileOpt = new Option<string>("--after-file", () => string.Empty, "Path containing proposed new content (file stays untouched until apply)");
        var addRationaleOpt = new Option<string>("--rationale", () => string.Empty, "Reason for the change");
    var addDeleteOpt = new Option<bool>("--delete", description: "Mark file for deletion (ignores --after-file)");
        var addJsonOpt = new Option<bool>("--json");
    add.AddOption(addIdOpt); add.AddOption(addPathOpt); add.AddOption(addAfterFileOpt); add.AddOption(addRationaleOpt); add.AddOption(addDeleteOpt); add.AddOption(addJsonOpt);
        add.SetHandler(async (System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            var id = ctx.ParseResult.GetValueForOption(addIdOpt)!;
            var path = ctx.ParseResult.GetValueForOption(addPathOpt)!;
            var afterFile = ctx.ParseResult.GetValueForOption(addAfterFileOpt)!;
            var rationale = ctx.ParseResult.GetValueForOption(addRationaleOpt)!;
            var isDelete = ctx.ParseResult.GetValueForOption(addDeleteOpt);
            var json = ctx.ParseResult.GetValueForOption(addJsonOpt);
            using var activity = Telemetry.ActivitySource.StartActivity("plan.add", ActivityKind.Client);
            var store = services.GetRequiredService<IPlanFileStore>();
            var rec = await store.GetAsync(id);
            var console = services.GetRequiredService<IAnsiConsole>();
            if (rec == null)
            {
                if (json) { JsonOutput.Write(console, new { schema = Rendering.Schemas.PlanAddV1, error = new { code = "PlanNotFound", message = "Plan not found" } }); return; }
                if (!OutputContext.IsQuiet()) console.MarkupLine(ConsoleStyles.Error("Plan not found")); return;
            }
            string? beforeContent = null;
            if (!isDelete)
            {
                if (!File.Exists(path))
                {
                    if (json) { JsonOutput.Write(console, new { schema = Rendering.Schemas.PlanAddV1, error = new { code = "FileMissing", message = $"File not found: {path}" } }); return; }
                    if (!OutputContext.IsQuiet()) console.MarkupLine(ConsoleStyles.Error($"File not found: {path}")); return;
                }
                beforeContent = await File.ReadAllTextAsync(path);
            }
            string? afterContent = null;
            if (!isDelete && !string.IsNullOrWhiteSpace(afterFile))
            {
                if (!File.Exists(afterFile)) { if (json) { JsonOutput.Write(console, new { schema = Rendering.Schemas.PlanAddV1, error = new { code = "AfterFileMissing", message = $"After file not found: {afterFile}" } }); return; } if (!OutputContext.IsQuiet()) console.MarkupLine(ConsoleStyles.Error($"After file not found: {afterFile}")); return; }
                afterContent = await File.ReadAllTextAsync(afterFile);
            }
            var existing = rec.Files.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = new PlanFileChange { Path = path };
                rec.Files.Add(existing);
            }
            existing.IsDelete = isDelete;
            existing.BeforeContent = beforeContent;
            existing.HashBefore = beforeContent != null ? PlanFileStore.Sha256(beforeContent) : null;
            existing.Rationale = string.IsNullOrWhiteSpace(rationale) ? existing.Rationale : rationale;
            if (!isDelete && afterContent != null)
            {
                existing.AfterContent = afterContent;
                existing.HashAfter = PlanFileStore.Sha256(afterContent);
                existing.Diff = DiffBuilder.Unified(beforeContent ?? string.Empty, afterContent, path);
            }
            if (isDelete)
            {
                existing.AfterContent = null; // ensure not treated as modification
                existing.HashAfter = null;
                existing.Diff = $"Deletion staged for {path}";
            }
            await store.SaveAsync(rec);
            if (json)
            {
                var dto = new
                {
                    schema = Rendering.Schemas.PlanAddV1,
                    planId = rec.Definition.Id,
                    file = new
                    {
                        path,
                        staged = true,
                        hasAfter = afterContent != null,
                        isDelete = isDelete,
                        hashBefore = existing.HashBefore,
                        hashAfter = existing.HashAfter,
                        diffGenerated = !string.IsNullOrWhiteSpace(existing.Diff)
                    },
                    rationale = string.IsNullOrWhiteSpace(existing.Rationale) ? null : existing.Rationale
                };
                JsonOutput.Write(console, dto);
            }
            else
            {
                var mode = isDelete ? "for deletion" : (afterContent!=null?"with after version":"(before snapshot only)");
                if (!OutputContext.IsQuiet()) console.MarkupLine($"Staged {ConsoleStyles.Accent(Path.GetFileName(path))} {mode}");
            }
        });
        plan.AddCommand(add);
    }

    private static void BuildList(Command plan, IServiceProvider services)
    {
        var list = new Command("list", "List recent plans");
        var takeOpt = new Option<int>("--take", () => 20);
        var jsonOpt = new Option<bool>("--json", "Emit JSON (schema: plan.list.v1)");
        list.AddOption(takeOpt); list.AddOption(jsonOpt);
        list.SetHandler(async (System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("plan.list", ActivityKind.Client);
            var take = ctx.ParseResult.GetValueForOption(takeOpt);
            var json = ctx.ParseResult.GetValueForOption(jsonOpt);
            var store = services.GetRequiredService<IPlanFileStore>();
            var items = await store.ListAsync(take);
            if (json)
            {
                JsonOutput.Write(services.GetRequiredService<IAnsiConsole>(), new { schema = Rendering.Schemas.PlanListV1, plans = items });
                return;
            }
            var console = services.GetService<IAnsiConsole>();
            if (items.Count == 0) { if (!OutputContext.IsQuiet()) console?.MarkupLine(ConsoleStyles.Warn("No plans found.")); return; }
            var table = new Table().RoundedBorder().Title(ConsoleStyles.PanelTitle("Plans"));
            table.AddColumn("Id").AddColumn("Created").AddColumn("Goal");
            foreach (var p in items)
            {
                var shortId = p.Id.Length>10? p.Id[..10]+"…":p.Id;
                table.AddRow(ConsoleStyles.Accent(shortId), p.CreatedUtc.ToString("u"), p.Goal);
            }
            if (!OutputContext.IsQuiet()) console?.Write(table);
        });
        plan.AddCommand(list);
    }

    private static void BuildShow(Command plan, IServiceProvider services)
    {
        var show = new Command("show", "Show a plan");
        var idOpt = new Option<string>("--id") { IsRequired = true };
        var jsonOpt = new Option<bool>("--json", "Emit JSON (schema: plan.show.v1)");
        show.AddOption(idOpt); show.AddOption(jsonOpt);
        show.SetHandler(async (System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("plan.show", ActivityKind.Client);
            var id = ctx.ParseResult.GetValueForOption(idOpt)!;
            var json = ctx.ParseResult.GetValueForOption(jsonOpt);
            var store = services.GetRequiredService<IPlanFileStore>();
            var rec = await store.GetAsync(id);
            var console = services.GetRequiredService<IAnsiConsole>();
            if (rec == null)
            {
                if (json) { JsonOutput.Write(console, new { schema = Rendering.Schemas.PlanShowV1, error = new { code = "PlanNotFound", message = "Plan not found" } }); return; }
                if (!OutputContext.IsQuiet()) console.MarkupLine(ConsoleStyles.Error("Plan not found")); return;
            }
            if (json)
            {
                JsonOutput.Write(console, new { schema = Rendering.Schemas.PlanShowV1, plan = rec });
                return;
            }
            var pretty = System.Text.Json.JsonSerializer.Serialize(rec, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            if (!OutputContext.IsQuiet()) console.Write(new Panel(new Markup(ConsoleStyles.Escape(pretty))).Header(ConsoleStyles.PanelTitle(id)).RoundedBorder());
        });
        plan.AddCommand(show);
    }

    private static void BuildDiff(Command plan, IServiceProvider services)
    {
        var diff = new Command("diff", "Show unified diffs for a plan");
        var diffIdOpt = new Option<string>("--id") { IsRequired = true };
        var diffJsonOpt = new Option<bool>("--json", "Emit JSON (schema: plan.diff.v1)");
        diff.AddOption(diffIdOpt); diff.AddOption(diffJsonOpt);
        diff.SetHandler(async (string id, bool json) =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("plan.diff", ActivityKind.Client);
            var store = services.GetRequiredService<IPlanFileStore>();
            var rec = await store.GetAsync(id);
            var console = services.GetRequiredService<IAnsiConsole>();
            if (rec == null) { if (!OutputContext.IsQuiet()) console.MarkupLine(ConsoleStyles.Error("Plan not found")); return; }
            if (json)
            {
                JsonOutput.Write(console, new { schema = Rendering.Schemas.PlanDiffV1, planId = id, diffs = rec.Files.Select(f => new { f.Path, f.Diff }).ToArray() });
                return;
            }
            if (rec.Files.Count == 0) { if (!OutputContext.IsQuiet()) console.MarkupLine(ConsoleStyles.Warn("Plan has no file changes.")); return; }
            foreach (var f in rec.Files)
            {
                if (!string.IsNullOrWhiteSpace(f.Diff))
                {
                    if (!OutputContext.IsQuiet()) console.Write(new Panel(new Markup("[silver]"+ConsoleStyles.Escape(f.Diff)+"[/]")).Header(ConsoleStyles.PanelTitle(f.Path)).RoundedBorder());
                }
            }
        }, diffIdOpt, diffJsonOpt);
        plan.AddCommand(diff);
    }

    private static void BuildApply(Command plan, IServiceProvider services)
    {
        var apply = new Command("apply", "Apply a plan's changes (creates backups and checks drift)");
        var applyIdOpt = new Option<string>("--id") { IsRequired = true };
        var dryRunOpt = new Option<bool>("--dry-run", () => false);
        var forceOpt = new Option<bool>("--force", () => false, "Apply even if drift detected");
        var applyJsonOpt = new Option<bool>("--json");
        apply.AddOption(applyIdOpt); apply.AddOption(dryRunOpt); apply.AddOption(forceOpt); apply.AddOption(applyJsonOpt);
        apply.SetHandler(async (System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            var id = ctx.ParseResult.GetValueForOption(applyIdOpt)!;
            var dryRun = ctx.ParseResult.GetValueForOption(dryRunOpt);
            var force = ctx.ParseResult.GetValueForOption(forceOpt);
            var json = ctx.ParseResult.GetValueForOption(applyJsonOpt);
            using var activity = Telemetry.ActivitySource.StartActivity("plan.apply", ActivityKind.Client);
            var store = services.GetRequiredService<IPlanFileStore>();
            var rec = await store.GetAsync(id);
            var console = services.GetRequiredService<IAnsiConsole>();
            if (rec == null)
            {
                if (json) { JsonOutput.Write(console, new { schema = Rendering.Schemas.PlanApplyV1, error = new { code = "PlanNotFound", message = "Plan not found" } }); return; }
                if (!OutputContext.IsQuiet()) console.MarkupLine(ConsoleStyles.Error("Plan not found")); return;
            }
            if (rec.Files.Count == 0)
            {
                if (json) { JsonOutput.Write(console, new { schema = Rendering.Schemas.PlanApplyV1, error = new { code = "NoChanges", message = "No changes to apply" } }); return; }
                if (!OutputContext.IsQuiet()) console.MarkupLine(ConsoleStyles.Warn("No changes to apply.")); return;
            }
            int applied = 0; int skipped = 0; int drift = 0;
            var backupRoot = Path.Combine(ConfigPaths.PlanBackupsDirectory, id + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
            if (!dryRun) Directory.CreateDirectory(backupRoot);
            var changes = new List<object>();
            foreach (var f in rec.Files)
            {
                string action = string.Empty; bool hadDrift = false; string? backupPath = null;
                if (f.IsDelete)
                {
                    if (!File.Exists(f.Path)) { skipped++; changes.Add(new { path = f.Path, action = "skip-missing", hadDrift, backupPath }); continue; }
                    if (!dryRun && File.Exists(f.Path))
                    {
                        try
                        {
                            var rel = Path.GetFileName(f.Path);
                            backupPath = Path.Combine(backupRoot, rel!);
                            File.Copy(f.Path, backupPath!, overwrite: true);
                        }
                        catch { }
                    }
                    try
                    {
                        if (!dryRun) File.Delete(f.Path);
                        applied++; action = dryRun ? "dry-run-delete" : "deleted";
                    }
                    catch { skipped++; action = "delete-error"; }
                    changes.Add(new { path = f.Path, action, hadDrift, backupPath });
                    continue;
                }
                if (f.AfterContent == null) { skipped++; continue; }
                if (!string.IsNullOrWhiteSpace(f.HashBefore) && File.Exists(f.Path))
                {
                    try
                    {
                        var current = await File.ReadAllTextAsync(f.Path);
                        var currHash = PlanFileStore.Sha256(current);
                        if (!string.Equals(currHash, f.HashBefore, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!force)
                            {
                                drift++; skipped++; hadDrift = true; action = "skipped-drift"; changes.Add(new { path = f.Path, action, hadDrift, backupPath }); continue;
                            }
                            hadDrift = true;
                        }
                    }
                    catch { }
                }
                if (dryRun) { applied++; action = "dry-run"; changes.Add(new { path = f.Path, action, hadDrift, backupPath }); continue; }
                try
                {
                    var dir = Path.GetDirectoryName(f.Path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    if (!dryRun && File.Exists(f.Path))
                    {
                        try
                        {
                            var rel = Path.GetFileName(f.Path);
                            backupPath = Path.Combine(backupRoot, rel!);
                            File.Copy(f.Path, backupPath!, overwrite: true);
                        }
                        catch { }
                    }
                    File.WriteAllText(f.Path, f.AfterContent);
                    applied++; action = "applied"; changes.Add(new { path = f.Path, action, hadDrift, backupPath });
                }
                catch { skipped++; action = "skipped-error"; changes.Add(new { path = f.Path, action, hadDrift, backupPath }); }
            }
            var driftNote = drift>0? ", drift " + drift : string.Empty;
            if (json)
            {
                var jsonPayload = new
                {
                    schema = Rendering.Schemas.PlanApplyV1,
                    planId = rec.Definition.Id,
                    dryRun,
                    force,
                    summary = new { applied, skipped, drift, backedUp = changes.Count(c => ((dynamic)c).backupPath != null) },
                    changes
                };
                JsonOutput.Write(console, jsonPayload);
            }
            else
            {
                if (!OutputContext.IsQuiet()) console.MarkupLine($"Applied {ConsoleStyles.Accent(applied.ToString())} changes (skipped {skipped}{driftNote}).");
                if (drift>0 && !force && !OutputContext.IsQuiet()) console.MarkupLine(ConsoleStyles.Warn("Drift detected; rerun with --force to override."));
            }
        });
        plan.AddCommand(apply);
    }
}
