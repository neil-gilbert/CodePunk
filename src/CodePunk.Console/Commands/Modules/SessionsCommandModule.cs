using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using Spectre.Console;
using Microsoft.Extensions.DependencyInjection;
using CodePunk.Console.Themes;
using CodePunk.Console.Stores;
using CodePunk.Core.Abstractions;

namespace CodePunk.Console.Commands.Modules;

internal sealed class SessionsCommandModule : ICommandModule
{
    public void Register(RootCommand root, IServiceProvider services)
    {
        root.Add(BuildSessions(services));
    }
    private static Command BuildSessions(IServiceProvider services)
    {
        var sessions = new Command("sessions", "Manage and inspect chat sessions");
        var list = new Command("list", "List recent sessions");
        var takeOpt = new Option<int>("--take", () => 20, "Limit number of sessions");
        var jsonOpt = new Option<bool>("--json", "Emit JSON (schema: sessions.list.v1)");
        list.AddOption(takeOpt); list.AddOption(jsonOpt);
        list.SetHandler(async (InvocationContext ctx) =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("sessions.list", ActivityKind.Client);
            try
            {
                var take = ctx.ParseResult.GetValueForOption(takeOpt);
                var json = ctx.ParseResult.GetValueForOption(jsonOpt);
                var store = services.GetRequiredService<ISessionFileStore>();
                var metas = await store.ListAsync(take);
                if (json)
                {
                    Rendering.JsonOutput.Write(services.GetRequiredService<IAnsiConsole>(), new { schema = Rendering.Schemas.SessionsListV1, sessions = metas });
                    return;
                }
                var console = services.GetService<IAnsiConsole>();
                if (metas.Count == 0)
                {
                    console?.MarkupLine(ConsoleStyles.Warn("No sessions found."));
                    return;
                }
                var table = new Table().RoundedBorder().Title(ConsoleStyles.PanelTitle("Sessions"));
                table.AddColumn("Id"); table.AddColumn("Title"); table.AddColumn("Agent"); table.AddColumn("Model"); table.AddColumn(new TableColumn("Msgs").Centered()); table.AddColumn("Updated");
                foreach (var m in metas)
                {
                    var shortId = m.Id.Length > 10 ? m.Id[..10] + "…" : m.Id;
                    table.AddRow(ConsoleStyles.Accent(shortId), m.Title ?? "(untitled)", string.IsNullOrWhiteSpace(m.Agent)?"[grey]-[/]":m.Agent!, m.Model ?? "[grey](default)[/]" , m.MessageCount.ToString(), m.LastUpdatedUtc.ToString("u"));
                }
                console?.Write(table);
            }
            catch (Exception ex)
            {
                ctx.ExitCode = 1;
                services.GetRequiredService<IAnsiConsole>().MarkupLine(ConsoleStyles.Error("sessions list error: "+ConsoleStyles.Escape(ex.Message)));
            }
            await Task.CompletedTask;
        });
        var show = new Command("show", "Show a session transcript");
        var idOpt = new Option<string>("--id") { IsRequired = true };
        var jsonOptShow = new Option<bool>("--json", "Emit JSON (schema: sessions.show.v1)");
        show.AddOption(idOpt); show.AddOption(jsonOptShow);
        show.SetHandler(async (InvocationContext ctx) =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("sessions.show", ActivityKind.Client);
            var id = ctx.ParseResult.GetValueForOption(idOpt);
            if (string.IsNullOrWhiteSpace(id)) { ctx.ExitCode = 1; services.GetRequiredService<IAnsiConsole>().MarkupLine(ConsoleStyles.Error("Session not found")); return; }
            var json = ctx.ParseResult.GetValueForOption(jsonOptShow);
            var store = services.GetRequiredService<ISessionFileStore>();
            var rec = await store.GetAsync(id);
            if (rec == null) { ctx.ExitCode = 0; services.GetRequiredService<IAnsiConsole>().MarkupLine(ConsoleStyles.Error("Session not found")); return; }
            if (json)
            {
                Rendering.JsonOutput.Write(services.GetRequiredService<IAnsiConsole>(), new { schema = Rendering.Schemas.SessionsShowV1, session = rec });
                return;
            }
            var console = services.GetService<IAnsiConsole>();
            var panelContent = new System.Text.StringBuilder();
            panelContent.AppendLine($"Title: {rec.Metadata.Title}");
            panelContent.AppendLine($"Id: {rec.Metadata.Id}");
            panelContent.AppendLine($"Agent: {rec.Metadata.Agent ?? "-"}");
            panelContent.AppendLine($"Model: {rec.Metadata.Model ?? "(default)"}");
            panelContent.AppendLine($"Messages: {rec.Messages.Count}");
            foreach (var m in rec.Messages) panelContent.AppendLine($"[{m.Role}] {m.Content.Replace("\n"," ")}");
            console?.Write(new Panel(new Markup(ConsoleStyles.Escape(panelContent.ToString()))).Header(ConsoleStyles.PanelTitle(rec.Metadata.Title ?? rec.Metadata.Id)).RoundedBorder());
        });
        var load = new Command("load", "Load a session id for reference");
        var loadIdOpt = new Option<string>("--id") { IsRequired = true };
        load.AddOption(loadIdOpt);
        load.SetHandler(async (string id) =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("sessions.load", ActivityKind.Client);
            var store = services.GetRequiredService<ISessionFileStore>();
            var rec = await store.GetAsync(id);
            var console = services.GetRequiredService<IAnsiConsole>();
            if (rec == null)
            {
                System.Environment.ExitCode = 0;
                console.MarkupLine(ConsoleStyles.Error("Session not found"));
            }
            else console.MarkupLine($"Loaded {ConsoleStyles.Accent(rec.Metadata.Title ?? rec.Metadata.Id)}");
        }, loadIdOpt);
        sessions.AddCommand(list); sessions.AddCommand(show); sessions.AddCommand(load);
        return sessions;
    }
}
