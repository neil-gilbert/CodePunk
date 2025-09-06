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

namespace CodePunk.Console.Commands;

internal static class RootCommandFactory
{
    public static RootCommand Create(IServiceProvider services)
    {
        var root = new RootCommand("CodePunk CLI")
        {
            BuildRun(services),
            BuildAuth(services),
            BuildAgent(services),
            BuildModels(services)
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
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    sessionId = await sessionStore.CreateAsync(TrimTitle(message), agent, model);
                }
                else if (await sessionStore.GetAsync(sessionId) == null)
                {
                    sessionId = await sessionStore.CreateAsync(TrimTitle(message), agent, model);
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
                console.MarkupLine($"[dim]Session: {sessionId}[/]");
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
            if (string.IsNullOrWhiteSpace(key))
            {
                System.Console.Write("Enter API key: ");
                key = System.Console.ReadLine() ?? string.Empty;
            }
            await store.SetAsync(provider, key);
            System.Console.WriteLine($"Stored key for provider '{provider}'.");
        }, providerOpt, keyOpt);
        var list = new Command("list", "List authenticated providers");
        list.SetHandler(async () =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("auth.list", ActivityKind.Client);
            var store = services.GetRequiredService<IAuthStore>();
            var map = await store.LoadAsync();
            if (map.Count == 0) { System.Console.WriteLine("No providers authenticated."); return; }
            foreach (var kv in map)
            {
                var masked = kv.Value.Length <= 8 ? new string('*', kv.Value.Length) : kv.Value[..4] + new string('*', kv.Value.Length-4);
                System.Console.WriteLine($"{kv.Key}\t{masked}");
            }
        });
        var logoutProviderOpt = new Option<string>("--provider") { IsRequired = true };
        var logout = new Command("logout", "Remove stored provider key") { logoutProviderOpt };
        logout.SetHandler(async (string provider) =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("auth.logout", ActivityKind.Client);
            activity?.SetTag("provider", provider);
            var store = services.GetRequiredService<IAuthStore>();
            await store.RemoveAsync(provider);
            System.Console.WriteLine($"Removed provider '{provider}'.");
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
            System.Console.WriteLine($"Agent '{name}' created.");
        }, nameOpt, providerOpt, modelOpt, promptFileOpt, overwriteOpt);
        var list = new Command("list", "List agents");
        list.SetHandler(async () =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("agent.list", ActivityKind.Client);
            var store = services.GetRequiredService<IAgentStore>();
            var defs = await store.ListAsync();
            if (!defs.Any()) { System.Console.WriteLine("No agents defined."); return; }
            foreach (var d in defs)
                System.Console.WriteLine($"{d.Name}\t{d.Provider}\t{d.Model ?? "(default model)"}");
        });
        var showNameOpt = new Option<string>("--name") { IsRequired = true };
        var show = new Command("show", "Show agent definition") { showNameOpt };
        show.SetHandler(async (string name) =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("agent.show", ActivityKind.Client);
            activity?.SetTag("agent.name", name);
            var store = services.GetRequiredService<IAgentStore>();
            var def = await store.GetAsync(name);
            if (def == null) { System.Console.WriteLine("Not found"); return; }
            System.Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(def, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }, showNameOpt);
        var deleteNameOpt = new Option<string>("--name") { IsRequired = true };
        var delete = new Command("delete", "Delete agent") { deleteNameOpt };
        delete.SetHandler(async (string name) =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("agent.delete", ActivityKind.Client);
            activity?.SetTag("agent.name", name);
            var store = services.GetRequiredService<IAgentStore>();
            await store.DeleteAsync(name);
            System.Console.WriteLine($"Deleted agent '{name}'.");
        }, deleteNameOpt);
        agent.AddCommand(create); agent.AddCommand(list); agent.AddCommand(show); agent.AddCommand(delete); return agent;
    }

    private static Command BuildModels(IServiceProvider services)
    {
        var jsonOpt = new Option<bool>("--json", "Output JSON");
        var cmd = new Command("models", "List available models from configured providers") { jsonOpt };
        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var json = ctx.ParseResult.GetValueForOption(jsonOpt);
            var llm = services.GetRequiredService<ILLMService>();
            var providers = llm.GetProviders();
            var rows = new List<(string Provider,string Id,string Name,int Context,int MaxTokens,bool Tools,bool Streaming)>();
            foreach (var p in providers.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
                foreach (var m in p.Models.OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase))
                    rows.Add((p.Name, m.Id, m.Name, m.ContextWindow, m.MaxTokens, m.SupportsTools, m.SupportsStreaming));
            var writer = ctx.Console.Out;
            if (json)
            {
                var jsonOut = System.Text.Json.JsonSerializer.Serialize(rows.Select(r => new { provider = r.Provider, id = r.Id, name = r.Name, context = r.Context, maxTokens = r.MaxTokens, tools = r.Tools, streaming = r.Streaming }), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                writer.Write(jsonOut + "\n");
                return;
            }
            if (providers.Count == 0)
            {
                writer.Write("No providers available. Authenticate first: codepunk auth login --provider <name> --key <APIKEY>\n");
                return;
            }
            if (rows.Count == 0)
            {
                writer.Write("No models found.\n");
                return;
            }
            writer.Write("PROVIDER\tMODEL ID\tNAME\tCTX\tMAX\tTOOLS\tSTREAM\n");
            foreach (var r in rows)
            {
                writer.Write($"{r.Provider}\t{r.Id}\t{r.Name}\t{r.Context}\t{r.MaxTokens}\t{(r.Tools ? "y" : "-")}\t{(r.Streaming ? "y" : "-")}\n");
            }
            await Task.CompletedTask;
        });
        return cmd;
    }

    // Exposed only for unit testing the models command logic without constructing full root.
    internal static Command CreateModelsCommandForTests(IServiceProvider services) => BuildModels(services);

    private static string TrimTitle(string input)
    {
        var oneLine = input.Replace("\n", " ").Trim();
        return oneLine.Length <= 60 ? oneLine : oneLine[..60];
    }
}
