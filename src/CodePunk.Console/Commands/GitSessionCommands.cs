using CodePunk.Console.Themes;
using CodePunk.Core.GitSession;
using Spectre.Console;

namespace CodePunk.Console.Commands;

public class AcceptSessionCommand : ChatCommand
{
    private readonly IGitSessionService _sessionService;

    public override string Name => "accept-session";
    public override string Description => "Accept and commit all AI changes from the current session";
    public override string[] Aliases => ["accept"];

    public AcceptSessionCommand(IGitSessionService sessionService)
    {
        _sessionService = sessionService;
    }

    public override async Task<CommandResult> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var session = await _sessionService.GetCurrentSessionAsync(cancellationToken);

        if (session == null)
        {
            AnsiConsole.MarkupLine(ConsoleStyles.Error("No active git session to accept"));
            return CommandResult.Ok();
        }

        var commitMessage = args.Length > 0
            ? string.Join(" ", args)
            : $"AI Session: Applied {session.ToolCallCommits.Count} changes";

        var success = await _sessionService.AcceptSessionAsync(commitMessage, cancellationToken);

        if (success)
        {
            AnsiConsole.MarkupLine(ConsoleStyles.Success($"Session accepted and committed: {session.ToolCallCommits.Count} tool calls merged"));
        }
        else
        {
            AnsiConsole.MarkupLine(ConsoleStyles.Error("Failed to accept session. Check logs for details."));
        }

        return CommandResult.Ok();
    }
}

public class RejectSessionCommand : ChatCommand
{
    private readonly IGitSessionService _sessionService;

    public override string Name => "reject-session";
    public override string Description => "Reject and discard all AI changes from the current session";
    public override string[] Aliases => ["reject"];

    public RejectSessionCommand(IGitSessionService sessionService)
    {
        _sessionService = sessionService;
    }

    public override async Task<CommandResult> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var session = await _sessionService.GetCurrentSessionAsync(cancellationToken);

        if (session == null)
        {
            AnsiConsole.MarkupLine(ConsoleStyles.Error("No active git session to reject"));
            return CommandResult.Ok();
        }

        var success = await _sessionService.RejectSessionAsync(cancellationToken);

        if (success)
        {
            AnsiConsole.MarkupLine(ConsoleStyles.Success($"Session rejected: {session.ToolCallCommits.Count} changes discarded"));
        }
        else
        {
            AnsiConsole.MarkupLine(ConsoleStyles.Error("Failed to reject session. Check logs for details."));
        }

        return CommandResult.Ok();
    }
}

public class SessionStatusCommand : ChatCommand
{
    private readonly IGitSessionService _sessionService;

    public override string Name => "session-status";
    public override string Description => "Show current git session status and commits";
    public override string[] Aliases => ["session", "git-status"];

    public SessionStatusCommand(IGitSessionService sessionService)
    {
        _sessionService = sessionService;
    }

    public override async Task<CommandResult> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var console = AnsiConsole.Console;
        var session = await _sessionService.GetCurrentSessionAsync(cancellationToken);

        if (session == null)
        {
            console.MarkupLine(ConsoleStyles.Dim("No active git session"));
            if (_sessionService.IsEnabled)
            {
                console.MarkupLine(ConsoleStyles.Dim("Git session tracking is enabled. Changes will be tracked when AI makes modifications."));
            }
            else
            {
                console.MarkupLine(ConsoleStyles.Dim("Git session tracking is disabled"));
            }
            return CommandResult.Ok();
        }

        console.WriteLine();
        console.Write(ConsoleStyles.HeaderRule("Git Session Status"));
        console.WriteLine();

        var table = new Table()
            .AddColumn("Property")
            .AddColumn("Value")
            .BorderColor(Color.Grey);

        table.AddRow("Session ID", ConsoleStyles.Dim(session.SessionId[..8]));
        table.AddRow("Shadow Branch", ConsoleStyles.Accent(session.ShadowBranch));
        table.AddRow("Original Branch", ConsoleStyles.Accent(session.OriginalBranch));
        table.AddRow("Started", session.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        table.AddRow("Tool Calls", session.ToolCallCommits.Count.ToString());

        if (session.StashId != null)
        {
            table.AddRow("Stashed Changes", ConsoleStyles.Success("Yes"));
        }

        console.Write(table);
        console.WriteLine();

        if (session.ToolCallCommits.Any())
        {
            console.Write(ConsoleStyles.HeaderRule("Tool Call Commits"));
            console.WriteLine();

            var commitsTable = new Table()
                .AddColumn("Tool")
                .AddColumn("Commit")
                .AddColumn("Time")
                .AddColumn("Files")
                .BorderColor(Color.Grey);

            foreach (var commit in session.ToolCallCommits)
            {
                var timeAgo = GetTimeAgo(commit.CommittedAt);
                var filesCount = commit.FilesChanged.Count > 0 ? commit.FilesChanged.Count.ToString() : "-";

                commitsTable.AddRow(
                    ConsoleStyles.Accent(commit.ToolName),
                    ConsoleStyles.Dim(commit.CommitHash[..8]),
                    ConsoleStyles.Dim(timeAgo),
                    filesCount
                );
            }

            console.Write(commitsTable);
            console.WriteLine();
        }

        console.MarkupLine(ConsoleStyles.Dim("Use /accept-session to commit changes or /reject-session to discard"));
        console.WriteLine();

        return CommandResult.Ok();
    }

    private static string GetTimeAgo(DateTimeOffset timestamp)
    {
        var elapsed = DateTimeOffset.UtcNow - timestamp;

        return elapsed.TotalMinutes switch
        {
            < 1 => "just now",
            < 60 => $"{(int)elapsed.TotalMinutes}m ago",
            < 1440 => $"{(int)elapsed.TotalHours}h ago",
            _ => $"{(int)elapsed.TotalDays}d ago"
        };
    }
}
