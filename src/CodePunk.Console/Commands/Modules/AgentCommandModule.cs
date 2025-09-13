using System.CommandLine;
using System.Diagnostics;
using Spectre.Console;
using Microsoft.Extensions.DependencyInjection;
using CodePunk.Core.Abstractions;
using CodePunk.Console.Themes;
using CodePunk.Console.Stores;

namespace CodePunk.Console.Commands.Modules;

internal sealed class AgentCommandModule : ICommandModule
{
    public void Register(RootCommand root, IServiceProvider services)
    {
        root.Add(BuildAgent(services));
    }
    private static Command BuildAgent(IServiceProvider services)
    {
        var agent = new Command("agent", "Manage chat agents");
        var nameOpt = new Option<string>("--name") { IsRequired = true };
        var providerOpt = new Option<string>("--provider", () => string.Empty, "Default provider");
        var modelOpt = new Option<string>("--model", () => string.Empty, "Default model");
        var promptFileOpt = new Option<string>("--prompt-file", () => string.Empty, "Prompt template file");
        var overwriteOpt = new Option<bool>("--overwrite", () => false, "Overwrite existing");
        var create = new Command("create", "Create or update an agent") { nameOpt, providerOpt, modelOpt, promptFileOpt, overwriteOpt };
        create.SetHandler(async (string name, string provider, string model, string promptFile, bool overwrite) =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("agent.create", ActivityKind.Client);
            activity?.SetTag("agent.name", name);
            var store = services.GetRequiredService<IAgentStore>();
            var console = services.GetRequiredService<IAnsiConsole>();
            string? prompt = null;
            if (!string.IsNullOrWhiteSpace(promptFile) && File.Exists(promptFile))
            {
                prompt = await File.ReadAllTextAsync(promptFile);
            }
            var def = new AgentDefinition
            {
                Name = name,
                Provider = string.IsNullOrWhiteSpace(provider) ? string.Empty : provider,
                Model = string.IsNullOrWhiteSpace(model) ? null : model,
                PromptFilePath = string.IsNullOrWhiteSpace(promptFile) ? null : promptFile
            };
            try
            {
                await store.CreateAsync(def, overwrite);
                console.MarkupLine($"{ConsoleStyles.Success("Agent created")} {ConsoleStyles.Accent(name)}");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                console.MarkupLine(ConsoleStyles.Warn($"Agent '{name}' already exists. Use --overwrite to replace."));
            }
        }, nameOpt, providerOpt, modelOpt, promptFileOpt, overwriteOpt);
        var list = new Command("list", "List agents");
        list.SetHandler(async () =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("agent.list", ActivityKind.Client);
            var store = services.GetRequiredService<IAgentStore>();
            var defs = await store.ListAsync();
            var console = services.GetRequiredService<IAnsiConsole>();
            if (!defs.Any()) { console.MarkupLine(ConsoleStyles.Warn("No agents defined.")); return; }
            var table = new Table().RoundedBorder().Title(ConsoleStyles.PanelTitle("Agents"));
            table.AddColumn("Name").AddColumn("Provider").AddColumn("Model");
            foreach (var d in defs)
            {
                table.AddRow(ConsoleStyles.Accent(d.Name), string.IsNullOrWhiteSpace(d.Provider)?"[grey]-[/]":d.Provider, d.Model ?? "[grey](default)" );
            }
            console.Write(table);
        });
        var showNameOpt = new Option<string>("--name") { IsRequired = true };
        var show = new Command("show", "Show agent definition") { showNameOpt };
        show.SetHandler(async (string name) =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("agent.show", ActivityKind.Client);
            activity?.SetTag("agent.name", name);
            var store = services.GetRequiredService<IAgentStore>();
            var def = await store.GetAsync(name);
            var console = services.GetRequiredService<IAnsiConsole>();
            if (def == null) { console.MarkupLine(ConsoleStyles.Error("Agent not found")); return; }
            var json = System.Text.Json.JsonSerializer.Serialize(def, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            console.Write(new Panel(new Markup($"[grey]{ConsoleStyles.Escape(json)}[/]"))
                .Header(ConsoleStyles.PanelTitle(def.Name))
                .RoundedBorder());
        }, showNameOpt);
        var deleteNameOpt = new Option<string>("--name") { IsRequired = true };
        var delete = new Command("delete", "Delete agent") { deleteNameOpt };
        delete.SetHandler(async (string name) =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("agent.delete", ActivityKind.Client);
            activity?.SetTag("agent.name", name);
            var store = services.GetRequiredService<IAgentStore>();
            await store.DeleteAsync(name);
            var console = services.GetRequiredService<IAnsiConsole>();
            console.MarkupLine($"{ConsoleStyles.Success("Deleted")} {ConsoleStyles.Accent(name)}");
        }, deleteNameOpt);
        agent.AddCommand(create); agent.AddCommand(list); agent.AddCommand(show); agent.AddCommand(delete); return agent;
    }
}
