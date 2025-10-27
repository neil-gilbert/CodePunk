using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using Spectre.Console;
using CodePunk.Infrastructure.Settings;
using Microsoft.Extensions.DependencyInjection;
using CodePunk.Core.Abstractions;
using CodePunk.Console.Themes;
using CodePunk.Core.Services;
using CodePunk.Console.Stores;

namespace CodePunk.Console.Commands.Modules;

internal sealed class ModelsCommandModule : ICommandModule
{
    public void Register(RootCommand root, IServiceProvider services)
    {
        root.Add(BuildModels(services));
    }
    private static Command BuildModels(IServiceProvider services)
    {
    var jsonOpt = new Option<bool>("--json", "Emit JSON (schema: models.list.v1)");
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
                    try { remote = await (p.FetchModelsAsync()); } catch { }
                    var models = (remote != null && remote.Count > 0) ? remote : p.Models;
                    foreach (var m in models.OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase))
                        rows.Add((p.Name, m.Id, m.Name, m.ContextWindow, m.MaxTokens, m.SupportsTools, m.SupportsStreaming, hasKey));
                }
                var writer = ctx.Console.Out;
                if (json)
                {
                    var payload = new { schema = Rendering.Schemas.ModelsListV1, models = rows.Select(r => new { provider = r.Provider, id = r.Id, name = r.Name, context = r.Context, maxTokens = r.MaxTokens, tools = r.Tools, streaming = r.Streaming, hasKey = r.HasKey }).ToArray() };
                    var ansi = services.GetService<IAnsiConsole>();
                    if (ansi != null)
                    {
                        Rendering.JsonOutput.Write(ansi, payload);
                    }
                    else
                    {
                        var jsonText = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        ctx.Console.Out.Write(jsonText + System.Environment.NewLine);
                    }
                    return;
                }
                var console = services.GetService<IAnsiConsole>();
                if (providers.Count == 0)
                {
                    var guidance = "No providers available. Authenticate first: codepunk auth login --provider <name> --key <APIKEY>";
                    if (!Rendering.OutputContext.IsQuiet()) console?.MarkupLine(ConsoleStyles.Warn(guidance));
                    return;
                }
                if (rows.Count == 0)
                {
                    if (!Rendering.OutputContext.IsQuiet()) console?.MarkupLine(ConsoleStyles.Warn("No models found."));
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
                    if (!Rendering.OutputContext.IsQuiet()) ctx.Console.Out.Write($"{r.Provider}\t{r.Id}\t{r.Name}\n");
                }
                if (!Rendering.OutputContext.IsQuiet()) console?.Write(table);
            }
            catch (Exception ex)
            {
                ctx.Console.Out.Write("models command error: " + ex.Message + "\n");
                ctx.ExitCode = 0;
            }
            await Task.CompletedTask;
        });
        return cmd;
    }
}
