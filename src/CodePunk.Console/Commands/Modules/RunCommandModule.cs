using System.CommandLine;
using System.Diagnostics;
using Spectre.Console;
using Microsoft.Extensions.DependencyInjection;
using CodePunk.Console.Chat;
using CodePunk.Console.Stores;
using CodePunk.Core.Chat;
using CodePunk.Console.Themes;

namespace CodePunk.Console.Commands.Modules;

internal sealed class RunCommandModule : ICommandModule
{
    private const int MaxTitleLength = 80;
    private static string TrimTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return title;
        var trimmed = title.Trim();
        return trimmed.Length > MaxTitleLength ? trimmed[..MaxTitleLength] : trimmed;
    }
    public void Register(RootCommand root, IServiceProvider services)
    {
        root.Add(BuildRun(services));
    }
    private static Command BuildRun(IServiceProvider services)
    {
        var messageArg = new Argument<string?>("message", () => null, "Prompt message (omit for interactive mode)");
        var sessionOpt = new Option<string>(new[]{"--session","-s"}, () => string.Empty, "Existing session id");
        var continueOpt = new Option<bool>(new[]{"--continue","-c"}, description: "Continue latest session");
        var agentOpt = new Option<string>(new[]{"--agent","-a"}, () => string.Empty, "Agent name override");
        var modelOpt = new Option<string>(new[]{"--model","-m"}, () => string.Empty, "Model override (provider/model)");
    var jsonOpt = new Option<bool>("--json", "Emit JSON (schema: run.execute.v1)");
    var cmd = new Command("run", "Run a one-shot prompt or continue a session") { messageArg, sessionOpt, continueOpt, agentOpt, modelOpt, jsonOpt };
    cmd.SetHandler(async (string? message, string session, bool cont, string agent, string model, bool json) =>
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
                if (cont && !string.IsNullOrEmpty(session)) { if (!Rendering.OutputContext.IsQuiet()) console.MarkupLine("[red]Cannot use both --continue and --session[/]"); return; }
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
                    if (def == null) { if (!Rendering.OutputContext.IsQuiet()) console.MarkupLine($"[red]Agent '{agent}' not found[/]"); }
                    else
                    {
                        activity?.SetTag("agent.name", def.Name);
                        if (!string.IsNullOrWhiteSpace(def.Provider)) { activity?.SetTag("agent.provider", def.Provider); providerOverride = def.Provider; }
                        if (!string.IsNullOrWhiteSpace(def.Model)) { activity?.SetTag("agent.model", def.Model); modelOverride ??= def.Model; }
                    }
                }
                services.GetRequiredService<InteractiveChatSession>().UpdateDefaults(providerOverride, modelOverride);
                var resolvedModelForStore = modelOverride ?? (string.IsNullOrWhiteSpace(model) ? null : model);
                if (string.IsNullOrWhiteSpace(sessionId)) sessionId = await sessionStore.CreateAsync(TrimTitle(message), agent, resolvedModelForStore);
                else if (await sessionStore.GetAsync(sessionId) == null) sessionId = await sessionStore.CreateAsync(TrimTitle(message), agent, resolvedModelForStore);
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
                        if (!Rendering.OutputContext.IsQuiet())
                        {
                            console.MarkupLine($"[grey]Using session:[/] {sessionId}");
                        }
                    if (json)
                    {
                        Rendering.JsonOutput.Write(console, new
                        {
                            schema = Rendering.Schemas.RunExecuteV1,
                            sessionId,
                            agent = string.IsNullOrWhiteSpace(agent) ? null : agent,
                            model = modelOverride ?? model,
                            request = new { message },
                            response = new { content = response },
                            usage = new { promptTokensApprox, completionTokensApprox, totalTokensApprox = promptTokensApprox + completionTokensApprox }
                        });
                        return;
                    }
                }
                if (json)
                {
                    Rendering.JsonOutput.Write(console, new
                    {
                        schema = Rendering.Schemas.RunExecuteV1,
                        sessionId,
                        agent = string.IsNullOrWhiteSpace(agent) ? null : agent,
                        model = modelOverride ?? model,
                        request = new { message },
                        response = new { content = response },
                        usage = new
                        {
                            promptTokensApprox = string.IsNullOrWhiteSpace(message)?0:(int)Math.Ceiling(message.Length/4.0),
                            completionTokensApprox = string.IsNullOrWhiteSpace(response)?0:(int)Math.Ceiling(response.Length/4.0),
                            totalTokensApprox = (string.IsNullOrWhiteSpace(message)?0:(int)Math.Ceiling(message.Length/4.0)) + (string.IsNullOrWhiteSpace(response)?0:(int)Math.Ceiling(response.Length/4.0))
                        }
                    });
                }
                else
                {
                    var shortId = sessionId.Length > 10 ? sessionId[..10] + "…" : sessionId;
                    if (!Rendering.OutputContext.IsQuiet()) console.MarkupLine($"{ConsoleStyles.Dim("Session:")} {ConsoleStyles.Accent(shortId)}");
                }
            }
            else { await chatLoop.RunAsync(); }
        }, messageArg, sessionOpt, continueOpt, agentOpt, modelOpt, jsonOpt);
        return cmd;
    }
}
