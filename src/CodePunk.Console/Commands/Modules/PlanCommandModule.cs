using System.CommandLine;
using System.Diagnostics;
using Spectre.Console;
using Microsoft.Extensions.DependencyInjection;
using CodePunk.Core.Abstractions;
using CodePunk.Console.Planning;
using CodePunk.Core.Chat;
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
        var goalOpt = new Option<string>("--goal") { IsRequired = true };
        var jsonOpt = new Option<bool>("--json");
        create.AddOption(goalOpt);
        create.AddOption(jsonOpt);
        create.SetHandler(async (System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            var goal = ctx.ParseResult.GetValueForOption(goalOpt);
            var json = ctx.ParseResult.GetValueForOption(jsonOpt);
            using var activity = Telemetry.ActivitySource.StartActivity("plan.create", ActivityKind.Client);
            var store = services.GetRequiredService<IPlanFileStore>();
            var id = await store.CreateAsync(goal!);
            var console = services.GetRequiredService<IAnsiConsole>();
            if (json)
            {
                var payload = new { schema = "plan.create.v1", planId = id, goal };
                JsonOutput.Write(console, payload);
                return;
            }
            console.MarkupLine($"Created plan {ConsoleStyles.Accent(id)}");
        });
        plan.AddCommand(create);
    }

    private static void BuildAdd(Command plan, IServiceProvider services)
    {
        var add = new Command("add", "Add (stage) a file change to a plan (optionally provide an after version)");
        var addIdOpt = new Option<string>("--id") { IsRequired = true };
        var addPathOpt = new Option<string>("--path") { IsRequired = true };
        var addAfterFileOpt = new Option<string>("--after-file", () => string.Empty, "Path containing proposed new content (file stays untouched until apply)");
        var addRationaleOpt = new Option<string>("--rationale", () => string.Empty, "Reason for the change");
        var addJsonOpt = new Option<bool>("--json");
        add.AddOption(addIdOpt); add.AddOption(addPathOpt); add.AddOption(addAfterFileOpt); add.AddOption(addRationaleOpt); add.AddOption(addJsonOpt);
        add.SetHandler(async (System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            var id = ctx.ParseResult.GetValueForOption(addIdOpt)!;
            var path = ctx.ParseResult.GetValueForOption(addPathOpt)!;
            var afterFile = ctx.ParseResult.GetValueForOption(addAfterFileOpt)!;
            var rationale = ctx.ParseResult.GetValueForOption(addRationaleOpt)!;
            var json = ctx.ParseResult.GetValueForOption(addJsonOpt);
            using var activity = Telemetry.ActivitySource.StartActivity("plan.add", ActivityKind.Client);
            var store = services.GetRequiredService<IPlanFileStore>();
            var rec = await store.GetAsync(id);
            var console = services.GetRequiredService<IAnsiConsole>();
            if (rec == null)
            {
                if (json) { JsonOutput.Write(console, new { schema = "plan.add.v1", error = new { code = "PlanNotFound", message = "Plan not found" } }); return; }
                console.MarkupLine(ConsoleStyles.Error("Plan not found")); return;
            }
            if (!File.Exists(path))
            {
                if (json) { JsonOutput.Write(console, new { schema = "plan.add.v1", error = new { code = "FileMissing", message = $"File not found: {path}" } }); return; }
                console.MarkupLine(ConsoleStyles.Error($"File not found: {path}")); return;
            }
            var beforeContent = await File.ReadAllTextAsync(path);
            string? afterContent = null;
            if (!string.IsNullOrWhiteSpace(afterFile))
            {
                if (!File.Exists(afterFile)) { if (json) { JsonOutput.Write(console, new { schema = "plan.add.v1", error = new { code = "AfterFileMissing", message = $"After file not found: {afterFile}" } }); return; } console.MarkupLine(ConsoleStyles.Error($"After file not found: {afterFile}")); return; }
                afterContent = await File.ReadAllTextAsync(afterFile);
            }
            var existing = rec.Files.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = new PlanFileChange { Path = path };
                rec.Files.Add(existing);
            }
            existing.BeforeContent = beforeContent;
            existing.HashBefore = PlanFileStore.Sha256(beforeContent);
            existing.Rationale = string.IsNullOrWhiteSpace(rationale) ? existing.Rationale : rationale;
            if (afterContent != null)
            {
                existing.AfterContent = afterContent;
                existing.HashAfter = PlanFileStore.Sha256(afterContent);
                existing.Diff = DiffBuilder.Unified(beforeContent, afterContent, path);
            }
            await store.SaveAsync(rec);
            if (json)
            {
                var dto = new
                {
                    schema = "plan.add.v1",
                    planId = rec.Definition.Id,
                    file = new
                    {
                        path,
                        staged = true,
                        hasAfter = afterContent != null,
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
                console.MarkupLine($"Staged {ConsoleStyles.Accent(Path.GetFileName(path))} {(afterContent!=null?"with after version":"(before snapshot only)")}");
            }
        });
        plan.AddCommand(add);
    }

    private static void BuildList(Command plan, IServiceProvider services)
    {
        var list = new Command("list", "List recent plans");
        var takeOpt = new Option<int>("--take", () => 20);
        var jsonOpt = new Option<bool>("--json");
        list.AddOption(takeOpt); list.AddOption(jsonOpt);
        list.SetHandler(async (System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("plan.list", ActivityKind.Client);
            var take = ctx.ParseResult.GetValueForOption(takeOpt);
            var json = ctx.ParseResult.GetValueForOption(jsonOpt);
            var store = services.GetRequiredService<IPlanFileStore>();
            var items = await store.ListAsync(take);
            var writer = ctx.Console.Out;
            if (json)
            {
                var jsonOut = System.Text.Json.JsonSerializer.Serialize(items, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                writer.Write(jsonOut + "\n"); return;
            }
            var console = services.GetService<IAnsiConsole>();
            if (items.Count == 0) { writer.Write("No plans found\n"); console?.MarkupLine(ConsoleStyles.Warn("No plans found.")); return; }
            var table = new Table().RoundedBorder().Title(ConsoleStyles.PanelTitle("Plans"));
            table.AddColumn("Id").AddColumn("Created").AddColumn("Goal");
            foreach (var p in items)
            {
                var shortId = p.Id.Length>10? p.Id[..10]+"…":p.Id;
                table.AddRow(ConsoleStyles.Accent(shortId), p.CreatedUtc.ToString("u"), p.Goal);
                writer.Write(p.Id+"\t"+p.Goal+"\n");
            }
            console?.Write(table);
        });
        plan.AddCommand(list);
    }

    private static void BuildShow(Command plan, IServiceProvider services)
    {
        var show = new Command("show", "Show a plan JSON");
        var idOpt = new Option<string>("--id") { IsRequired = true };
        show.AddOption(idOpt);
        show.SetHandler( async (string id) =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("plan.show", ActivityKind.Client);
            var store = services.GetRequiredService<IPlanFileStore>();
            var rec = await store.GetAsync(id);
            var console = services.GetRequiredService<IAnsiConsole>();
            if (rec == null) { console.MarkupLine(ConsoleStyles.Error("Plan not found")); return; }
            var json = System.Text.Json.JsonSerializer.Serialize(rec, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            console.Write(new Panel(new Markup(ConsoleStyles.Escape(json))).Header(ConsoleStyles.PanelTitle(id)).RoundedBorder());
        }, idOpt);
        plan.AddCommand(show);
    }

    private static void BuildDiff(Command plan, IServiceProvider services)
    {
        var diff = new Command("diff", "Show unified diffs for a plan");
        var diffIdOpt = new Option<string>("--id") { IsRequired = true };
        var diffJsonOpt = new Option<bool>("--json");
        diff.AddOption(diffIdOpt); diff.AddOption(diffJsonOpt);
        diff.SetHandler(async (string id, bool json) =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("plan.diff", ActivityKind.Client);
            var store = services.GetRequiredService<IPlanFileStore>();
            var rec = await store.GetAsync(id);
            var console = services.GetRequiredService<IAnsiConsole>();
            if (rec == null) { console.MarkupLine(ConsoleStyles.Error("Plan not found")); return; }
            if (json)
            {
                var jsonOut = System.Text.Json.JsonSerializer.Serialize(rec.Files.Select(f => new { f.Path, f.Diff }), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                console.WriteLine(jsonOut);
                return;
            }
            if (rec.Files.Count == 0) { console.MarkupLine(ConsoleStyles.Warn("Plan has no file changes.")); return; }
            foreach (var f in rec.Files)
            {
                if (!string.IsNullOrWhiteSpace(f.Diff))
                {
                    console.Write(new Panel(new Markup("[silver]"+ConsoleStyles.Escape(f.Diff)+"[/]")).Header(ConsoleStyles.PanelTitle(f.Path)).RoundedBorder());
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
                if (json) { JsonOutput.Write(console, new { schema = "plan.apply.v1", error = new { code = "PlanNotFound", message = "Plan not found" } }); return; }
                console.MarkupLine(ConsoleStyles.Error("Plan not found")); return;
            }
            if (rec.Files.Count == 0)
            {
                if (json) { JsonOutput.Write(console, new { schema = "plan.apply.v1", error = new { code = "NoChanges", message = "No changes to apply" } }); return; }
                console.MarkupLine(ConsoleStyles.Warn("No changes to apply.")); return;
            }
            int applied = 0; int skipped = 0; int drift = 0;
            var backupRoot = Path.Combine(ConfigPaths.PlanBackupsDirectory, id + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
            if (!dryRun) Directory.CreateDirectory(backupRoot);
            var changes = new List<object>();
            foreach (var f in rec.Files)
            {
                string action = string.Empty; bool hadDrift = false; string? backupPath = null;
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
                    schema = "plan.apply.v1",
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
                console.MarkupLine($"Applied {ConsoleStyles.Accent(applied.ToString())} changes (skipped {skipped}{driftNote}).");
                if (drift>0 && !force) console.MarkupLine(ConsoleStyles.Warn("Drift detected; rerun with --force to override."));
            }
        });
        plan.AddCommand(apply);
    }
}
