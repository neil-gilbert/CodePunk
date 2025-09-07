using CodePunk.Core.Abstractions;
using Spectre.Console;
using System.Linq;
using CodePunk.Console.Themes;

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
        console.Write(new Rule("[cyan]Available Commands[/]").LeftJustified());
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
        console.MarkupLine("[dim]Tip: Type your message directly to chat with AI, or use commands starting with /[/]");
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
    return Task.FromResult(CommandResult.ClearSession($"Starting new session: {Console.Themes.ConsoleStyles.Accent(sessionTitle)}"));
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
    return Task.FromResult(CommandResult.Exit(Console.Themes.ConsoleStyles.Dim("Goodbye! 👋")));
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

    public override string Name => "sessions";
    public override string Description => "Show recent chat sessions";
    public override string[] Aliases => ["s", "list"];

    public SessionsCommand(ISessionService sessionService)
    {
        _sessionService = sessionService;
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
            console.MarkupLine(ConsoleStyles.Warn("No sessions found. Start chatting to create your first session!"));
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
            table.AddRow(
                ConsoleStyles.Dim(session.Id[..8] + "…"),
                ConsoleStyles.Accent(session.Title),
                ConsoleStyles.Dim(session.CreatedAt.ToString("yyyy-MM-dd HH:mm")),
                session.MessageCount.ToString());
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
            return CommandResult.Error($"{Console.Themes.ConsoleStyles.Warn("Please provide a session ID.")} Use {Console.Themes.ConsoleStyles.Accent("/sessions")} to see available sessions.");
        }

        var sessionId = args[0];
        var session = await _sessionService.GetByIdAsync(sessionId, cancellationToken);
        
        if (session == null)
        {
            return CommandResult.Error($"Session not found: {Console.Themes.ConsoleStyles.Error(sessionId)}");
        }

        // Note: The actual session loading will be handled by the chat loop
    var shortId = session.Id.Length > 8 ? session.Id[..8] + "…" : session.Id;
    return CommandResult.Ok($"Loaded session: {Console.Themes.ConsoleStyles.Accent(session.Title)} {Console.Themes.ConsoleStyles.Dim("(ID: " + shortId + ")")}");
    }
}
