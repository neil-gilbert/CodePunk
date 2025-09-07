using CodePunk.Core.Abstractions;
using CodePunk.Core.Services;
using Spectre.Console;
using CodePunk.Console.Themes;

namespace CodePunk.Console.Commands;

/// <summary>
/// Lists available LLM models (usage: /models [provider])
/// </summary>
public class ModelsChatCommand : ChatCommand
{
    private readonly ILLMService _llm;
    public ModelsChatCommand(ILLMService llm) { _llm = llm; }
    public override string Name => "models";
    public override string Description => "List available models (optionally filter by provider: /models Anthropic)";
    public override string[] Aliases => Array.Empty<string>();

    public override async Task<CommandResult> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var providers = _llm.GetProviders() ?? Array.Empty<ILLMProvider>();
        if (providers.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No providers available. Authenticate first.[/]");
            return CommandResult.Ok("No providers available.");
        }

        string? filter = args.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(filter))
        {
            providers = providers.Where(p => string.Equals(p.Name, filter, StringComparison.OrdinalIgnoreCase)).ToList();
            if (providers.Count == 0)
            {
                AnsiConsole.MarkupLine($"[yellow]Provider not found: {filter}[/]");
                return CommandResult.Ok($"Provider not found: {filter}");
            }
        }

        var rows = new List<(string Provider, string Id, string Name, int Ctx, int Max, bool Tools, bool Stream)>();
        foreach (var p in providers.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            IReadOnlyList<LLMModel> remote = Array.Empty<LLMModel>();
            try { remote = await p.FetchModelsAsync(cancellationToken); } catch { }
            var models = (remote != null && remote.Count > 0) ? remote : p.Models;
            foreach (var m in models.OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase))
                rows.Add((p.Name, m.Id, m.Name, m.ContextWindow, m.MaxTokens, m.SupportsTools, m.SupportsStreaming));
        }

        if (rows.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No models found.[/]");
            return CommandResult.Ok("No models found.");
        }

        var table = new Table().RoundedBorder().Title(ConsoleStyles.PanelTitle(filter == null ? "Models" : $"Models ({filter})"));
        table.AddColumn("Provider");
        table.AddColumn("Model Id");
        table.AddColumn("Name");
        table.AddColumn(new TableColumn("Ctx").Centered());
        table.AddColumn(new TableColumn("Max").Centered());
        table.AddColumn(new TableColumn("Tools").Centered());
        table.AddColumn(new TableColumn("Stream").Centered());
        foreach (var r in rows)
        {
            table.AddRow(ConsoleStyles.Accent(r.Provider), r.Id, r.Name, r.Ctx.ToString(), r.Max.ToString(), r.Tools?"[green]✓[/]":"[grey]-[/]", r.Stream?"[green]✓[/]":"[grey]-[/]");
        }
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        return CommandResult.Ok(filter == null ? $"Models listed: {rows.Count}" : $"Models listed for {filter}: {rows.Count}");
    }
}
