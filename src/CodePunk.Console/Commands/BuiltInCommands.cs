using CodePunk.Core.Abstractions;
using Spectre.Console;
using System.Linq;
using CodePunk.Console.Themes;
using CodePunk.Core.Chat;

namespace CodePunk.Console.Commands;

/// <summary>
/// Shows help information for available commands
/// </summary>
public class HelpCommand : ChatCommand
{
    private IReadOnlyList<ChatCommand> _all = Array.Empty<ChatCommand>();

    public override string Name => "help";
    public override string Description => "Shows available commands and their usage";
    public override string[] Aliases => ["h", "?"];

    public void Initialize(IEnumerable<ChatCommand> all)
    {
        // Exclude self to avoid duplication
        _all = all.Where(c => c != this).ToList();
    }

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

        var commands = _all.OrderBy(c => c.Name).ToList();
        foreach (var command in commands)
        {
            var aliases = command.Aliases.Length > 0 ? string.Join(", ", command.Aliases.Select(a => $"/{a}")) : "-";
            table.AddRow($"[cyan]/{command.Name}[/]", $"[dim]{aliases}[/]", command.Description);
        }

        // Include self description at end
        table.AddRow("[cyan]/help[/]", "[dim]/h, /?[/]", Description);

        console.Write(table);
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

    public override Task<CommandResult> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var sessionTitle = args.Length > 0 
            ? string.Join(" ", args)
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
        table.AddColumn("ID");
        table.AddColumn("Title");
        table.AddColumn(new TableColumn("Created").Centered());
        table.AddColumn(new TableColumn("Msgs").Centered());

        foreach (var session in sessions)
        {
            // Fallback: compute live count if zero (covers legacy rows or if repository not updating count)
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
                ConsoleStyles.Dim(session.Id[..8] + "…"),
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

        // Note: The actual session loading will be handled by the chat loop
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
            // Single token: treat as provider OR model (heuristic: contains '-' assume model)
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
            // Support /use provider Anthropic model claude-3
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

        // Build a compact table to avoid raw markup showing and improve alignment
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
