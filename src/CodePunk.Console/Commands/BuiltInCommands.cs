using CodePunk.Core.Abstractions;
using Spectre.Console;
using CodePunk.Infrastructure.Settings;
using System.Linq;
using CodePunk.Console.Themes;
using CodePunk.Core.Chat;
using CodePunk.Console.Stores;
using CodePunk.Core.Services;

namespace CodePunk.Console.Commands;

/// <summary>
/// Shows help information for available commands
/// </summary>
public class HelpCommand : ChatCommand
{
    public override string Name => "help";
    public override string Description => "Shows available commands and their usage";
    public override string[] Aliases => ["h", "?"];

    public override Task<CommandResult> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var console = AnsiConsole.Console;

    console.WriteLine();
    console.Write(ConsoleStyles.HeaderRule("Commands"));
    console.WriteLine();

        var table = new Table()
            .AddColumn("Command")
            .AddColumn("Aliases")
            .AddColumn("Description")
            .BorderColor(Color.Grey);

        var commands = new[]
        {
            ("/clear", new[] { "/cls" }, "Clear the console screen"),
            ("/help", new[] { "/h", "/?" }, "Shows available commands and their usage"),
            ("/load", new[] { "/l" }, "Load a previous chat session"),
            ("/models", Array.Empty<string>(), "Manage AI models and providers"),
            ("/setup", Array.Empty<string>(), "Guided first-time setup (select provider & store key)"),
            ("/reload", Array.Empty<string>(), "Reload providers after adding keys"),
            ("/providers", Array.Empty<string>(), "List providers & persistence paths"),
            ("/new", new[] { "/n" }, "Start a new chat session"),
            ("/plan", Array.Empty<string>(), "Manage change plans: create | add | diff | apply | generate --ai"),
            ("/quit", new[] { "/q", "/exit" }, "Exit the application"),
            ("/sessions", new[] { "/s", "/list" }, "Show recent chat sessions"),
            ("/usage", new[] { "/tokens" }, "Show accumulated token usage & estimated cost for current session"),
            ("/use", new[] { "/u" }, "Set default provider and/or model")
        };

        string Escape(string? text) => string.IsNullOrEmpty(text) ? string.Empty : text.Replace("[", "[[").Replace("]", "]]");
        
        foreach (var (command, aliases, description) in commands)
        {
            var aliasText = aliases.Length > 0 ? string.Join(", ", aliases) : "-";
            table.AddRow($"[cyan]{command}[/]", $"[dim]{aliasText}[/]", Escape(description));
        }

        console.Write(table);
        console.WriteLine();
        console.MarkupLine(ConsoleStyles.Accent("First time here? Run /setup to configure a provider & store your API key."));
        console.WriteLine();
        console.MarkupLine(ConsoleStyles.Dim("Tip: Type your message directly to chat with AI, or use commands starting with /"));
        console.WriteLine();

        return Task.FromResult(CommandResult.Ok());
    }
}

/// <summary>
/// Starts a new chat session
/// </summary>
public class NewCommand : ChatCommand
{
    public override string Name => "new";
    public override string Description => "Start a new chat session";
    public override string[] Aliases => ["n"];

    private static string TrimTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return title;
        const int max = 80;
        var trimmed = title.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }

    public override Task<CommandResult> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var sessionTitle = args.Length > 0 
            ? TrimTitle(string.Join(" ", args))
            : $"Chat Session {DateTime.Now:yyyy-MM-dd HH:mm}";
    return Task.FromResult(CommandResult.ClearSession($"{ConsoleStyles.Success("✓")} New session: {ConsoleStyles.Accent(sessionTitle)}"));
    }
}

/// <summary>
/// Exits the application
/// </summary>
public class QuitCommand : ChatCommand
{
    public override string Name => "quit";
    public override string Description => "Exit the application";
    public override string[] Aliases => ["q", "exit"];

    public override Task<CommandResult> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
    return Task.FromResult(CommandResult.Exit(ConsoleStyles.Dim("Goodbye! 👋")));
    }
}

/// <summary>
/// Clears the console screen
/// </summary>
public class ClearCommand : ChatCommand
{
    public override string Name => "clear";
    public override string Description => "Clear the console screen";
    public override string[] Aliases => ["cls"];

    public override Task<CommandResult> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        AnsiConsole.Clear();
        return Task.FromResult(CommandResult.Ok());
    }
}

/// <summary>
/// Shows recent chat sessions
/// </summary>
public class SessionsCommand : ChatCommand
{
    private readonly ISessionService _sessionService;
    private readonly IMessageService _messageService;

    public override string Name => "sessions";
    public override string Description => "Show recent chat sessions";
    public override string[] Aliases => ["s", "list"];

    public SessionsCommand(ISessionService sessionService, IMessageService messageService)
    {
        _sessionService = sessionService;
        _messageService = messageService;
    }

    public override async Task<CommandResult> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var console = AnsiConsole.Console;
        
    console.WriteLine();
    console.Write(ConsoleStyles.HeaderRule("Recent Sessions"));
    console.WriteLine();

        var sessions = await _sessionService.GetRecentAsync(10, cancellationToken);
        
        if (!sessions.Any())
        {
            console.Write(ConsolePanels.Warn("No sessions found. Start chatting to create your first session!"));
            console.WriteLine();
            return CommandResult.Ok();
        }

    var table = new Table().RoundedBorder().Title(ConsoleStyles.PanelTitle("Recent"));
    var idWidth = sessions.Any() ? sessions.Max(s => s.Id.Length) : 8;
    table.AddColumn(new TableColumn("ID").LeftAligned().NoWrap().Width(idWidth));
        table.AddColumn("Title");
        table.AddColumn(new TableColumn("Created").Centered());
        table.AddColumn(new TableColumn("Msgs").Centered());

        foreach (var session in sessions)
        {
            int count = session.MessageCount;
            if (count == 0)
            {
                try
                {
                    var msgs = await _messageService.GetBySessionAsync(session.Id, cancellationToken);
                    count = msgs.Count;
                }
                catch { }
            }
            table.AddRow(
                ConsoleStyles.Dim(session.Id),
                ConsoleStyles.Accent(session.Title),
                ConsoleStyles.Dim(session.CreatedAt.ToString("yyyy-MM-dd HH:mm")),
                count.ToString());
        }

        console.Write(table);
        console.WriteLine();
        console.MarkupLine(ConsoleStyles.Dim("Tip: Use /load <session-id> to continue a previous conversation"));
        console.WriteLine();

        return CommandResult.Ok();
    }
}

/// <summary>
/// Loads a previous chat session
/// </summary>
public class LoadCommand : ChatCommand
{
    private readonly ISessionService _sessionService;

    public override string Name => "load";
    public override string Description => "Load a previous chat session";
    public override string[] Aliases => ["l"];

    public LoadCommand(ISessionService sessionService)
    {
        _sessionService = sessionService;
    }

    public override async Task<CommandResult> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0)
        {
            return CommandResult.Error("Please provide a session ID. Use /sessions to see available sessions.");
        }

        var sessionId = args[0];
        var session = await _sessionService.GetByIdAsync(sessionId, cancellationToken);
        
        if (session == null)
        {
            return CommandResult.Error($"Session not found: {sessionId}");
        }

    var shortId = session.Id.Length > 8 ? session.Id[..8] + "…" : session.Id;
    return CommandResult.Ok($"Loaded session: {ConsoleStyles.Accent(session.Title)} {ConsoleStyles.Dim("(ID: " + shortId + ")")}");
    }
}

/// <summary>
/// Updates default provider/model for subsequent AI calls
/// </summary>
public class UseCommand : ChatCommand
{
    private readonly InteractiveChatSession _chatSession;
    public override string Name => "use";
    public override string Description => "Set default provider and/or model (usage: /use provider Anthropic | /use model claude | /use Anthropic claude-3)";
    public override string[] Aliases => ["u"];

    public UseCommand(InteractiveChatSession chatSession)
    {
        _chatSession = chatSession;
    }

    public override Task<CommandResult> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (!_chatSession.IsActive)
        {
            return Task.FromResult(CommandResult.Error("No active session. Start one with /new."));
        }

        if (args.Length == 0)
        {
            return Task.FromResult(CommandResult.Error("Usage: /use <provider> <model> | /use provider <name> | /use model <name>"));
        }

        string? provider = null;
        string? model = null;

        if (args.Length == 1)
        {
            if (args[0].Contains('-', StringComparison.OrdinalIgnoreCase) || args[0].StartsWith("gpt", StringComparison.OrdinalIgnoreCase))
                model = args[0];
            else
                provider = args[0];
        }
        else if (args.Length == 2)
        {
            if (string.Equals(args[0], "provider", StringComparison.OrdinalIgnoreCase))
                provider = args[1];
            else if (string.Equals(args[0], "model", StringComparison.OrdinalIgnoreCase))
                model = args[1];
            else
            {
                provider = args[0];
                model = args[1];
            }
        }
        else if (args.Length >= 3)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "provider", StringComparison.OrdinalIgnoreCase))
                    provider = args[i + 1];
                else if (string.Equals(args[i], "model", StringComparison.OrdinalIgnoreCase))
                    model = args[i + 1];
            }
        }

        if (provider == null && model == null)
        {
            return Task.FromResult(CommandResult.Error("Could not parse provider/model arguments."));
        }

        _chatSession.UpdateDefaults(provider, model);
        var parts = new List<string>();
        if (provider != null) parts.Add($"provider = {ConsoleStyles.Accent(provider)}");
        if (model != null) parts.Add($"model = {ConsoleStyles.Accent(model)}");
        var msg = $"Updated defaults: {string.Join(", ", parts)}";
        return Task.FromResult(CommandResult.Ok(msg));
    }
}

/// <summary>
/// Displays current session token usage & cost
/// </summary>
public class UsageCommand : ChatCommand
{
    private readonly InteractiveChatSession _chatSession;
    public override string Name => "usage";
    public override string Description => "Show accumulated token usage & estimated cost for current session";
    public override string[] Aliases => ["tokens"];

    public UsageCommand(InteractiveChatSession chatSession)
    {
        _chatSession = chatSession;
    }

    public override Task<CommandResult> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (!_chatSession.IsActive)
            return Task.FromResult(CommandResult.Error("No active session."));

        var prompt = _chatSession.AccumulatedPromptTokens;
        var completion = _chatSession.AccumulatedCompletionTokens;
        var total = prompt + completion;
        var cost = _chatSession.AccumulatedCost;

        var table = new Table()
            .NoBorder()
            .AddColumn(new TableColumn("[grey]Metric[/]").LeftAligned())
            .AddColumn(new TableColumn("[grey]Value[/]").LeftAligned());

        string Accent(string s) => ConsoleStyles.Accent(s);

        table.AddRow("[grey]Prompt[/]", Accent(prompt.ToString()));
        table.AddRow("[grey]Completion[/]", Accent(completion.ToString()));
        table.AddRow("[grey]Total[/]", Accent(total.ToString()));
        table.AddEmptyRow();
        table.AddRow("[grey]Estimated Cost[/]", Accent(cost == 0 ? "$0" : cost.ToString("C")));

        var usagePanel = new Panel(table)
            .Header(ConsoleStyles.PanelTitle("Usage"))
            .BorderColor(Color.Grey54)
            .Expand();

        AnsiConsole.Write(usagePanel);
        AnsiConsole.WriteLine();
        return Task.FromResult(CommandResult.Ok());
    }
}

public class ProvidersCommand : ChatCommand
{
    private readonly ILLMService _llmService;
    private readonly InteractiveChatSession _chatSession;
    public override string Name => "providers";
    public override string Description => "List registered providers, defaults, and persistence paths";
    public override string[] Aliases => Array.Empty<string>();
    public ProvidersCommand(ILLMService llmService, InteractiveChatSession chatSession)
    {
        _llmService = llmService;
        _chatSession = chatSession;
    }
    public override Task<CommandResult> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var console = AnsiConsole.Console;
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Provider");
        table.AddColumn("Models (sample)");
        var providers = _llmService.GetProviders();
        if (providers.Count == 0)
        {
            console.MarkupLine(ConsoleStyles.Dim("No providers currently registered. Run /setup or /reload after adding keys."));
        }
        else
        {
            foreach (var p in providers)
            {
                var sample = string.Join(", ", p.Models.Take(5).Select(m => m.Id));
                table.AddRow(p.Name, string.IsNullOrEmpty(sample) ? "(none)" : sample);
            }
            console.Write(table);
        }
        console.WriteLine();
        console.MarkupLine($"Default Provider: [green]{_chatSession.DefaultProvider}[/]  Model: [green]{_chatSession.DefaultModel}[/]");
        // Show persistence paths
        try
        {
            console.MarkupLine(ConsoleStyles.Dim($"Auth file: {ConfigPaths.AuthFile}"));
            console.MarkupLine(ConsoleStyles.Dim($"Defaults file: {ConfigPaths.DefaultsFile}"));
        }
        catch { }
        console.WriteLine();
        return Task.FromResult(CommandResult.Ok());
    }
}

public class ReloadCommand : ChatCommand
{
    private readonly IServiceProvider _sp;
    public override string Name => "reload";
    public override string Description => "Reload provider registrations from stored credentials";
    public override string[] Aliases => Array.Empty<string>();
    public ReloadCommand(IServiceProvider sp) { _sp = sp; }
    public override async Task<CommandResult> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        try
        {
            var bootstrap = _sp.GetService(typeof(CodePunk.Infrastructure.Providers.ProviderBootstrapper)) as CodePunk.Infrastructure.Providers.ProviderBootstrapper;
            if (bootstrap == null) return CommandResult.Error("Bootstrap service unavailable.");
            await bootstrap.ApplyAsync(cancellationToken);
            return CommandResult.Ok("Providers reloaded. Use /models to list models.");
        }
        catch (Exception ex)
        {
            return CommandResult.Error("Reload failed: " + ex.Message);
        }
    }
}

/// <summary>
/// Guided first-time setup: select provider, enter API key, set defaults.
/// </summary>
public class SetupCommand : ChatCommand
{
    private readonly IAuthStore _authStore;
    private readonly IDefaultsStore _defaultsStore;
    private readonly InteractiveChatSession _chatSession;
    private readonly ILLMService _llmService;
    public override string Name => "setup";
    public override string Description => "Guided first-time setup (select provider & store key)";
    public override string[] Aliases => Array.Empty<string>();

    private readonly IServiceProvider? _serviceProvider;
    public SetupCommand(IAuthStore authStore, IDefaultsStore defaultsStore, InteractiveChatSession chatSession, ILLMService llmService, IServiceProvider serviceProvider)
    {
        _authStore = authStore;
        _defaultsStore = defaultsStore;
        _chatSession = chatSession;
        _llmService = llmService;
        _serviceProvider = serviceProvider;
    }

    public override async Task<CommandResult> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var console = AnsiConsole.Console;
        console.WriteLine();
        console.Write(ConsoleStyles.HeaderRule("First-Time Setup"));
        console.MarkupLine(ConsoleStyles.Dim("This will store an API key locally (auth.json) and set defaults."));
        console.WriteLine();

        // Provider selection
        var providers = _llmService.GetProviders();
        List<string> providerNames;
        if (providers.Count == 0)
        {
            // Static fallback list when none registered yet (hide openai until implemented)
            providerNames = new List<string> { "anthropic" };
            console.MarkupLine(ConsoleStyles.Dim("No providers registered yet. We'll register one after you enter a key."));
        }
        else
        {
            providerNames = providers.Select(p => p.Name)
                .Where(n => !string.Equals(n, "openai", StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (providerNames.Count == 0)
            {
                providerNames = new List<string> { "anthropic" };
            }
        }
        var provider = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(ConsoleStyles.Accent("Select provider"))
                .PageSize(10)
                .AddChoices(providerNames));

        // API key prompt (masked)
        var key = AnsiConsole.Prompt(
            new TextPrompt<string>(ConsoleStyles.Accent($"Enter API key for {provider}:"))
                .PromptStyle("silver")
                .Secret());
        try
        {
            await _authStore.SetAsync(provider, key, cancellationToken);
            console.MarkupLine(ConsoleStyles.Success("Stored") + " key for " + ConsoleStyles.Accent(provider));
        }
        catch (Exception ex)
        {
            return CommandResult.Error("Failed to store key: " + ex.Message);
        }

        // Default model selection (if multiple models)
        // If provider list was initially empty we need to attempt a dynamic bootstrap now
        if (providers.Count == 0 && _serviceProvider != null)
        {
            try
            {
                var bootstrap = _serviceProvider.GetService(typeof(CodePunk.Infrastructure.Providers.ProviderBootstrapper)) as CodePunk.Infrastructure.Providers.ProviderBootstrapper;
                if (bootstrap != null)
                {
                    await bootstrap.ApplyAsync(cancellationToken);
                }
            }
            catch { }
            providers = _llmService.GetProviders();
        }

        var selectedProvider = providers.FirstOrDefault(p => string.Equals(p.Name, provider, StringComparison.OrdinalIgnoreCase));
        List<string> models = new();
        if (selectedProvider != null)
        {
            models = selectedProvider.Models.Select(m => m.Id).ToList();
        }
        string? model = null;
        if (models.Count > 1)
        {
            model = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title(ConsoleStyles.Accent($"Select default model ({provider})"))
                    .PageSize(10)
                    .AddChoices(models));
        }
        else if (models.Count == 1)
        {
            model = models[0];
        }

    _chatSession.UpdateDefaults(provider, model);
    try { await _defaultsStore.SaveAsync(new CodePunkDefaults(provider, model), cancellationToken); } catch { }

        var summary = new Table().NoBorder();
        summary.AddColumn("Item"); summary.AddColumn("Value");
        summary.AddRow("Provider", ConsoleStyles.Accent(provider));
        if (!string.IsNullOrWhiteSpace(model)) summary.AddRow("Model", ConsoleStyles.Accent(model));
        summary.AddRow("Auth File", ConfigPaths.AuthFile);
        summary.AddRow("Defaults File", ConfigPaths.DefaultsFile);
        console.Write(summary);
        console.WriteLine();
        console.MarkupLine(ConsoleStyles.Dim("Setup complete. You can change later with /use or update keys with auth commands."));
        console.WriteLine();
        return CommandResult.Ok();
    }
}
