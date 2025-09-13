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
using CodePunk.Console.Planning;
using CodePunk.Console.Commands.Modules;

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
        var root = new RootCommand("CodePunk CLI");
   
        var modules = new ICommandModule[]
        {
            new RunCommandModule(),
            new AuthCommandModule(),
            new AgentCommandModule(),
            new ModelsCommandModule(),
            new SessionsCommandModule(),
            new PlanCommandModule()
        };
        foreach (var m in modules) m.Register(root, services);
        root.SetHandler(async () =>
        {
            var loop = services.GetRequiredService<InteractiveChatLoop>();
            await loop.RunAsync();
        });
        return root;
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
