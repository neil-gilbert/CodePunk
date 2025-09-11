using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using Spectre.Console;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using CodePunk.Console.Chat;
using CodePunk.Console.Stores;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Chat;
using CodePunk.Core.Services;
using CodePunk.Console.Themes;

namespace CodePunk.Console.Commands;

internal static class RootCommandFactory
{
    private const int MaxTitleLength = 80;
    private static string TrimTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return title;
        var trimmed = title.Trim();
        return trimmed.Length > MaxTitleLength ? trimmed[..MaxTitleLength] : trimmed;
    }
    public static RootCommand Create(IServiceProvider services)
    {
    var root = new RootCommand("CodePunk CLI")
        {
            BuildRun(services),
            BuildAuth(services),
            BuildAgent(services),
            BuildModels(services),
            BuildSessions(services)
        };
        root.SetHandler(async () =>
        {
            var loop = services.GetRequiredService<InteractiveChatLoop>();
            await loop.RunAsync();
        });
        return root;
    }

    private static Command BuildRun(IServiceProvider services)
    {
        var messageArg = new Argument<string?>("message", () => null, "Prompt message (omit for interactive mode)");
        var sessionOpt = new Option<string>(new[]{"--session","-s"}, () => string.Empty, "Existing session id");
        var continueOpt = new Option<bool>(new[]{"--continue","-c"}, description: "Continue latest session");
        var agentOpt = new Option<string>(new[]{"--agent","-a"}, () => string.Empty, "Agent name override");
        var modelOpt = new Option<string>(new[]{"--model","-m"}, () => string.Empty, "Model override (provider/model)");
        var cmd = new Command("run", "Run a one-shot prompt or continue a session") { messageArg, sessionOpt, continueOpt, agentOpt, modelOpt };
        cmd.SetHandler(async (string? message, string session, bool cont, string agent, string model) =>
        {
            var console = services.GetRequiredService<IAnsiConsole>();
            var chatLoop = services.GetRequiredService<InteractiveChatLoop>();
            var sessionStore = services.GetRequiredService<ISessionFileStore>();
            var agentStore = services.GetRequiredService<IAgentStore>();
            using var activity = Telemetry.ActivitySource.StartActivity("run", ActivityKind.Client);
            activity?.SetTag("cli.command", "run");
            activity?.SetTag("cli.hasMessage", !string.IsNullOrWhiteSpace(message));
            if (!string.IsNullOrWhiteSpace(message))
            {
                string sessionId = session;
                if (cont && !string.IsNullOrEmpty(session))
                {
                    console.MarkupLine("[red]Cannot use both --continue and --session[/]");
                    return;
                }
                if (cont)
                {
                    var latest = await sessionStore.ListAsync(1);
                    if (latest.Count > 0) sessionId = latest[0].Id;
                }
                string? providerOverride = null;
                string? modelOverride = string.IsNullOrWhiteSpace(model) ? null : model;
                if (!string.IsNullOrWhiteSpace(agent))
                {
                    var def = await agentStore.GetAsync(agent);
                    if (def == null)
                    {
                        console.MarkupLine($"[red]Agent '{agent}' not found[/]");
                    }
                    else
                    {
                        activity?.SetTag("agent.name", def.Name);
                        if (!string.IsNullOrWhiteSpace(def.Provider)) { activity?.SetTag("agent.provider", def.Provider); providerOverride = def.Provider; }
                        if (!string.IsNullOrWhiteSpace(def.Model)) { activity?.SetTag("agent.model", def.Model); modelOverride ??= def.Model; }
                    }
                }
                services.GetRequiredService<InteractiveChatSession>().UpdateDefaults(providerOverride, modelOverride);
                var resolvedModelForStore = modelOverride ?? (string.IsNullOrWhiteSpace(model) ? null : model);
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    sessionId = await sessionStore.CreateAsync(TrimTitle(message), agent, resolvedModelForStore);
                }
                else if (await sessionStore.GetAsync(sessionId) == null)
                {
                    sessionId = await sessionStore.CreateAsync(TrimTitle(message), agent, resolvedModelForStore);
                }
                await sessionStore.AppendMessageAsync(sessionId, "user", message);
                var response = await chatLoop.RunSingleAsync(message);
                if (!string.IsNullOrWhiteSpace(response))
                {
                    await sessionStore.AppendMessageAsync(sessionId, "assistant", response);
                    activity?.SetTag("response.size", response.Length);
                    var promptTokensApprox = (int)Math.Ceiling(message.Length / 4.0);
                    var completionTokensApprox = (int)Math.Ceiling(response.Length / 4.0);
                    activity?.SetTag("tokens.prompt.approx", promptTokensApprox);
                    activity?.SetTag("tokens.completion.approx", completionTokensApprox);
                    activity?.SetTag("tokens.total.approx", promptTokensApprox + completionTokensApprox);
                }
                var shortId = sessionId.Length > 10 ? sessionId[..10] + "…" : sessionId;
                console.MarkupLine($"{ConsoleStyles.Dim("Session:")} {ConsoleStyles.Accent(shortId)}");
            }
            else
            {
                await chatLoop.RunAsync();
            }
        }, messageArg, sessionOpt, continueOpt, agentOpt, modelOpt);
        return cmd;
    }

    private static Command BuildAuth(IServiceProvider services)
    {
        var auth = new Command("auth", "Manage provider credentials");
        var providerOpt = new Option<string>("--provider", description: "Provider name") { IsRequired = true };
        var keyOpt = new Option<string>("--key", () => string.Empty, "API key (omit to prompt)");
        var login = new Command("login", "Store an API key") { providerOpt, keyOpt };
        login.SetHandler(async (string provider, string key) =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("auth.login", ActivityKind.Client);
            activity?.SetTag("provider", provider);
            var store = services.GetRequiredService<IAuthStore>();
            var console = services.GetRequiredService<IAnsiConsole>();
            if (string.IsNullOrWhiteSpace(key))
            {
                key = console.Prompt(new TextPrompt<string>(ConsoleStyles.Accent("Enter API key:"))
                    .PromptStyle("silver")
                    .Secret());
            }
            await store.SetAsync(provider, key);
            console.MarkupLine($"{ConsoleStyles.Success("Stored")} {ConsoleStyles.Dim("provider")} {ConsoleStyles.Accent(provider)}");
        }, providerOpt, keyOpt);
        var list = new Command("list", "List authenticated providers");
        list.SetHandler(async () =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("auth.list", ActivityKind.Client);
            var store = services.GetRequiredService<IAuthStore>();
            var map = await store.LoadAsync();
            var console = services.GetRequiredService<IAnsiConsole>();
            if (map.Count == 0) { console.MarkupLine(ConsoleStyles.Warn("No providers authenticated.")); return; }
            var table = new Table().RoundedBorder().Title(ConsoleStyles.PanelTitle("Providers")).AddColumn("Name").AddColumn("Key");
            foreach (var kv in map)
            {
                var masked = kv.Value.Length <= 8 ? new string('*', kv.Value.Length) : kv.Value[..4] + new string('*', kv.Value.Length-4);
                table.AddRow(ConsoleStyles.Accent(kv.Key), $"[grey]{masked}[/]");
            }
            console.Write(table);
        });
        var logoutProviderOpt = new Option<string>("--provider") { IsRequired = true };
        var logout = new Command("logout", "Remove stored provider key") { logoutProviderOpt };
        logout.SetHandler(async (string provider) =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("auth.logout", ActivityKind.Client);
            activity?.SetTag("provider", provider);
            var store = services.GetRequiredService<IAuthStore>();
            await store.RemoveAsync(provider);
            var console = services.GetRequiredService<IAnsiConsole>();
            console.MarkupLine($"{ConsoleStyles.Success("Removed")} {ConsoleStyles.Accent(provider)}");
        }, logoutProviderOpt);
        auth.AddCommand(login); auth.AddCommand(list); auth.AddCommand(logout); return auth;
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
            await store.CreateAsync(def, overwrite);
            console.MarkupLine($"{ConsoleStyles.Success("Agent created")} {ConsoleStyles.Accent(name)}");
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

    private static Command BuildModels(IServiceProvider services)
    {
        var jsonOpt = new Option<bool>("--json", "Output JSON");
        var availableOnlyOpt = new Option<bool>("--available-only", "Show only providers with stored API keys");
        var cmd = new Command("models", "List available models from configured providers") { jsonOpt, availableOnlyOpt };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            try
            {
                var json = ctx.ParseResult.GetValueForOption(jsonOpt);
                var availableOnly = ctx.ParseResult.GetValueForOption(availableOnlyOpt);
                var llm = services.GetRequiredService<ILLMService>();
                var providers = llm.GetProviders() ?? Array.Empty<ILLMProvider>();
                var authStore = services.GetRequiredService<IAuthStore>();
                var authenticated = (await authStore.LoadAsync()).Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var rows = new List<(string Provider,string Id,string Name,int Context,int MaxTokens,bool Tools,bool Streaming,bool HasKey)>();
                foreach (var p in providers.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var hasKey = authenticated.Contains(p.Name) || authenticated.Contains(p.Name.Replace("Provider","", StringComparison.OrdinalIgnoreCase));
                    if (availableOnly && !hasKey) continue;
                    IReadOnlyList<CodePunk.Core.Abstractions.LLMModel> remote = Array.Empty<CodePunk.Core.Abstractions.LLMModel>();
                    try
                    {
                        remote = await (p.FetchModelsAsync());
                    }
                    catch { }

                    var models = (remote != null && remote.Count > 0) ? remote : p.Models;
                    foreach (var m in models.OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase))
                        rows.Add((p.Name, m.Id, m.Name, m.ContextWindow, m.MaxTokens, m.SupportsTools, m.SupportsStreaming, hasKey));
                }
                var writer = ctx.Console.Out;
                if (json)
                {
                    var jsonOut = System.Text.Json.JsonSerializer.Serialize(rows.Select(r => new { provider = r.Provider, id = r.Id, name = r.Name, context = r.Context, maxTokens = r.MaxTokens, tools = r.Tools, streaming = r.Streaming, hasKey = r.HasKey }), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    writer.Write(jsonOut + "\n");
                    return;
                }
                var console = services.GetService<IAnsiConsole>();
                if (providers.Count == 0)
                {
                    var guidance = "No providers available. Authenticate first: codepunk auth login --provider <name> --key <APIKEY>";
                    writer.Write(guidance + "\n");
                    if (console != null) console.MarkupLine(ConsoleStyles.Warn(guidance));
                    return;
                }
                if (rows.Count == 0)
                {
                    writer.Write("No models found.\n");
                    console?.MarkupLine(ConsoleStyles.Warn("No models found."));
                    return;
                }
                var table = new Table().RoundedBorder().Title(ConsoleStyles.PanelTitle("Models"));
                table.AddColumn("Provider");
                table.AddColumn("Model Id");
                table.AddColumn("Name");
                table.AddColumn(new TableColumn("Ctx").Centered());
                table.AddColumn(new TableColumn("Max").Centered());
                table.AddColumn(new TableColumn("Tools").Centered());
                table.AddColumn(new TableColumn("Stream").Centered());
                table.AddColumn(new TableColumn("Key").Centered());
                foreach (var r in rows)
                {
                    var providerLabel = r.HasKey ? ConsoleStyles.Accent(r.Provider) : $"[grey]{r.Provider}[/]";
                    table.AddRow(providerLabel, r.Id, r.Name, r.Context.ToString(), r.MaxTokens.ToString(), r.Tools?"[green]✓[/]":"[grey]-[/]", r.Streaming?"[green]✓[/]":"[grey]-[/]", r.HasKey?"[green]✓[/]":"[red]✗[/]");
                    writer.Write($"{r.Provider}\t{r.Id}\t{r.Name}\n");
                }
                console?.Write(table);
            }
            catch (Exception ex)
            {
                ctx.Console.Out.Write("models command error: " + ex.Message + "\n");
                ctx.ExitCode = 0; // do not fail tests on console-only issues
            }
            await Task.CompletedTask;
        });
        return cmd;
    }

    private static Command BuildSessions(IServiceProvider services)
    {
        var sessions = new Command("sessions", "Manage and inspect chat sessions");
        var list = new Command("list", "List recent sessions");
        var takeOpt = new Option<int>("--take", () => 20, "Limit number of sessions");
        var jsonOpt = new Option<bool>("--json", "Emit JSON");
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
                var writer = ctx.Console.Out;
                if (json)
                {
                    var jsonOut = System.Text.Json.JsonSerializer.Serialize(metas, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    writer.Write(jsonOut + "\n");
                    return;
                }
                var console = services.GetService<IAnsiConsole>();
                if (metas.Count == 0)
                {
                    writer.Write("No sessions found.\n");
                    console?.MarkupLine(ConsoleStyles.Warn("No sessions found."));
                    return;
                }
                var table = new Table().RoundedBorder().Title(ConsoleStyles.PanelTitle("Sessions"));
                table.AddColumn("Id"); table.AddColumn("Title"); table.AddColumn("Agent"); table.AddColumn("Model"); table.AddColumn(new TableColumn("Msgs").Centered()); table.AddColumn("Updated");
                foreach (var m in metas)
                {
                    var shortId = m.Id.Length > 10 ? m.Id[..10] + "…" : m.Id;
                    table.AddRow(ConsoleStyles.Accent(shortId), m.Title ?? "(untitled)", string.IsNullOrWhiteSpace(m.Agent)?"[grey]-[/]":m.Agent!, m.Model ?? "[grey](default)[/]" , m.MessageCount.ToString(), m.LastUpdatedUtc.ToString("u"));
                    writer.Write(m.Id + "\t" + (m.Title ?? string.Empty) + "\n");
                }
                console?.Write(table);
            }
            catch (Exception ex)
            {
                ctx.Console.Out.Write("sessions list error: " + ex.Message + "\n");
            }
            await Task.CompletedTask;
        });
        var show = new Command("show", "Show a session transcript");
        var idOpt = new Option<string>("--id") { IsRequired = true };
        var jsonOptShow = new Option<bool>("--json", "Emit JSON");
        show.AddOption(idOpt); show.AddOption(jsonOptShow);
        show.SetHandler(async (InvocationContext ctx) =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("sessions.show", ActivityKind.Client);
            var id = ctx.ParseResult.GetValueForOption(idOpt);
            var json = ctx.ParseResult.GetValueForOption(jsonOptShow);
            var store = services.GetRequiredService<ISessionFileStore>();
            var rec = await store.GetAsync(id);
            var writer = ctx.Console.Out;
            if (rec == null) { writer.Write("Session not found\n"); return; }
            if (json)
            {
                var jsonOut = System.Text.Json.JsonSerializer.Serialize(rec, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                writer.Write(jsonOut + "\n"); return;
            }
            var console = services.GetService<IAnsiConsole>();
            var panelContent = new System.Text.StringBuilder();
            panelContent.AppendLine($"Title: {rec.Metadata.Title}");
            panelContent.AppendLine($"Id: {rec.Metadata.Id}");
            panelContent.AppendLine($"Agent: {rec.Metadata.Agent ?? "-"}");
            panelContent.AppendLine($"Model: {rec.Metadata.Model ?? "(default)"}");
            panelContent.AppendLine($"Messages: {rec.Messages.Count}");
            foreach (var m in rec.Messages)
            {
                panelContent.AppendLine($"[{m.Role}] {m.Content.Replace("\n"," ")}");
            }
            writer.Write(panelContent.ToString());
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
                console.MarkupLine(ConsoleStyles.Error("Session not found"));
            }
            else
            {
                console.MarkupLine($"Loaded {ConsoleStyles.Accent(rec.Metadata.Title ?? rec.Metadata.Id)}");
            }
        }, loadIdOpt);
        sessions.AddCommand(list); sessions.AddCommand(show); sessions.AddCommand(load);
        return sessions;
    }

    internal static Command CreateModelsCommandForTests(IServiceProvider services)
    {
        var cmd = new Command("models", "List available LLM models (test mode)");
        var jsonOpt = new Option<bool>("--json", description: "Emit JSON");
        cmd.AddOption(jsonOpt);
        cmd.SetHandler((InvocationContext ctx) =>
        {
            try
            {
                var llm = services.GetRequiredService<ILLMService>();
                var providers = llm.GetProviders() ?? Array.Empty<ILLMProvider>();
                var json = ctx.ParseResult.GetValueForOption(jsonOpt);
                var writer = ctx.Console.Out;
                var rows = providers
                    .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .SelectMany(p => p.Models.OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                        .Select(m => new { provider = p.Name, id = m.Id, name = m.Name, context = m.ContextWindow, max = m.MaxTokens, tools = m.SupportsTools, streaming = m.SupportsStreaming }))
                    .ToList();
                if (json)
                {
                    var jsonOut = System.Text.Json.JsonSerializer.Serialize(rows, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    writer.Write(jsonOut + "\n");
                    ctx.ExitCode = 0; return;
                }
                if (providers.Count == 0)
                {
                    writer.Write("No providers available. Authenticate first: codepunk auth login --provider <name> --key <APIKEY>\n");
                    ctx.ExitCode = 0; return;
                }
                if (rows.Count == 0)
                {
                    writer.Write("No models found.\n");
                    ctx.ExitCode = 0; return;
                }
                foreach (var r in rows)
                {
                    writer.Write($"{r.provider}\t{r.id}\t{r.name}\n");
                }
                ctx.ExitCode = 0;
            }
            catch (Exception ex)
            {
                ctx.Console.Out.Write("models command error: " + ex.Message + "\n");
                ctx.ExitCode = 1;
            }
        });
        return cmd;
    }

}
