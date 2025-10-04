using System.Text;
using CodePunk.Console.Commands;
using CodePunk.Console.Rendering;
using CodePunk.Core.Chat;
using CodePunk.Core.GitSession;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace CodePunk.Console.Chat;

/// <summary>
/// Main interactive chat loop that handles user input and AI responses
/// </summary>
public class InteractiveChatLoop
{
    private readonly InteractiveChatSession _chatSession;
    private readonly CommandProcessor _commandProcessor;
    private readonly StreamingResponseRenderer _renderer;
    private readonly IAnsiConsole _console;
    private readonly IGitSessionService? _gitSessionService;
    private readonly ILogger<InteractiveChatLoop> _logger;

    private bool _shouldExit;
    private bool _shouldCreateNewSession;

    public InteractiveChatLoop(
        InteractiveChatSession chatSession,
        CommandProcessor commandProcessor,
        StreamingResponseRenderer renderer,
        IAnsiConsole console,
        ILogger<InteractiveChatLoop> logger,
        IGitSessionService? gitSessionService = null)
    {
        _chatSession = chatSession;
        _commandProcessor = commandProcessor;
        _renderer = renderer;
        _console = console;
        _gitSessionService = gitSessionService;
        _logger = logger;
    }

    /// <summary>
    /// Executes a single prompt outside full interactive loop (used by 'run' one-shot mode).
    /// Ensures a session exists and writes the streamed response to console output directly.
    /// </summary>
    public async Task<string> RunSingleAsync(string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;
        await EnsureActiveSessionAsync(cancellationToken);
        _renderer.StartStreaming();

        var sb = new StringBuilder();

        await foreach (var chunk in _chatSession.SendMessageStreamAsync(message, cancellationToken))
        {
            _renderer.ProcessChunk(chunk);
            if (!string.IsNullOrEmpty(chunk.ContentDelta)) sb.Append(chunk.ContentDelta);
        }
        _renderer.CompleteStreaming();

        // Check for active git session and prompt for accept/reject
        await PromptGitSessionApprovalAsync(cancellationToken);

        return sb.ToString();
    }

    /// <summary>
    /// Starts the interactive chat loop
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        ShowWelcome();
        
        await EnsureActiveSessionAsync(cancellationToken);

        try
        {
            while (!_shouldExit && !cancellationToken.IsCancellationRequested)
            {
                if (_shouldCreateNewSession)
                {
                    await CreateNewSessionAsync(cancellationToken);
                    _shouldCreateNewSession = false;
                }

                var input = await GetUserInputAsync();
                
                if (string.IsNullOrWhiteSpace(input))
                    continue;

                if (_commandProcessor.IsCommand(input))
                {
                    await HandleCommandAsync(input, cancellationToken);
                }
                else
                {
                    await HandleChatMessageAsync(input, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Chat loop cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in chat loop");
            _console.MarkupLine($"[red]Unexpected error: {ex.Message}[/]");
        }

        ShowGoodbye();
    }

    /// <summary>
    /// Shows the welcome message
    /// </summary>
    private void ShowWelcome()
    {
        _console.Clear();

        var colors = new[] { "#00FFFF", "#33CCFF", "#6699FF", "#9933FF", "#FF00FF", "#FF66CC" };
        var header = HeaderLogo.CyberSmallCaps;

        var sb = new StringBuilder();
        for (var i = 0; i < header.Length; i++)
        {
            var color = colors[i % colors.Length];
            sb.AppendLine($"[{color}]{header[i]}[/]");
        }

        var headerPanel = new Panel(new Markup(sb.ToString()))
            .Border(BoxBorder.Double)
            .BorderStyle(new Style(Color.Aqua))
            .Header("[bold #FF66FF]Welcome to CodePunk[/]", Justify.Center)
            .Padding(1, 1);

        _console.Write(new Align(headerPanel, HorizontalAlignment.Center, VerticalAlignment.Top));

        _console.MarkupLine("[dim]AI-powered coding assistant - Interactive Chat[/]");
        _console.WriteLine();
        
        _console.Write(new Rule("[cyan]Welcome to CodePunk[/]"));
        _console.MarkupLine("Start chatting with AI or type [cyan]/help[/] for commands");
        _console.MarkupLine("First time? Run [cyan]/setup[/] to select a provider & store your API key");
        _console.MarkupLine("Added a key? Use [cyan]/reload[/] to refresh providers");
        _console.MarkupLine("Type [cyan]/new[/] to start a new session");
        _console.MarkupLine("Press [cyan]Ctrl+C[/] or type [cyan]/quit[/] to exit");
        _console.WriteLine();
    }

    /// <summary>
    /// Shows goodbye message
    /// </summary>
    private void ShowGoodbye()
    {
        _console.WriteLine();
        _console.MarkupLine("[dim]Thanks for using CodePunk![/]");
    }

    /// <summary>
    /// Ensures we have an active chat session
    /// </summary>
    private async Task EnsureActiveSessionAsync(CancellationToken cancellationToken)
    {
        if (!_chatSession.IsActive)
        {
            await CreateNewSessionAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Creates a new chat session
    /// </summary>
    private async Task CreateNewSessionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var sessionTitle = $"Chat Session {DateTime.Now:yyyy-MM-dd HH:mm}";
            var session = await _chatSession.StartNewSessionAsync(sessionTitle, cancellationToken);
            
            _console.MarkupLine($"[green]✓[/] Started new session: [cyan]{session.Title}[/]");
            _console.MarkupLine($"[dim]Session ID: {session.Id[..8]}...[/]");
            _console.WriteLine();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create new session");
            _console.MarkupLine($"[red]Failed to create new session: {ex.Message}[/]");
        }
    }

    /// <summary>
    /// Gets user input with a nice prompt
    /// </summary>
    private async Task<string> GetUserInputAsync()
    {
    await Task.Yield();
        _console.Write(new Rule().LeftJustified());
        _console.Markup("[bold green] You[/]");
        
        if (_chatSession.IsActive)
        {
            var sid = _chatSession.CurrentSession!.Id[..8];
            var provider = _chatSession.DefaultProvider;
            var model = _chatSession.DefaultModel;
            var iter = _chatSession.IsToolLoopActive ? $" • tools {_chatSession.ToolIteration}/{_chatSession.MaxToolIterations}" : string.Empty;
            _console.Markup($" [dim](Session: {sid} • {provider}:{model}{iter})[/]");
        }
        
        _console.WriteLine();

        var lines = new List<string>();
        var isFirstLine = true;
        
        while (true)
        {
            var prompt = isFirstLine ? "> " : "  ";
            _console.Markup($"[dim]{prompt}[/]");
            
            var line = System.Console.ReadLine() ?? string.Empty;
            
            if (string.IsNullOrWhiteSpace(line) && lines.Count > 0)
                break;
                
            if (lines.Count == 0 && _commandProcessor.IsCommand(line))
            if (isFirstLine && line.StartsWith('/'))
            {
                return line;
            }
            
            lines.Add(line);
            isFirstLine = false;
            
            if (lines.Count == 1 && !string.IsNullOrWhiteSpace(line))
                break;
        }

        return string.Join("\n", lines).Trim();
    }

    /// <summary>
    /// Handles command execution
    /// </summary>
    private async Task HandleCommandAsync(string input, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _commandProcessor.ExecuteCommandAsync(input, cancellationToken);
            
            if (!string.IsNullOrEmpty(result.Message))
            {
                _console.MarkupLine(result.Message);
                _console.WriteLine();
            }

            if (result.ShouldExit)
            {
                _shouldExit = true;
            }
            else if (result.ShouldClearSession)
            {
                _chatSession.ClearSession();
                _shouldCreateNewSession = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling command: {Input}", input);
            _console.MarkupLine($"[red]Error executing command: {ex.Message}[/]");
            _console.WriteLine();
        }
    }

    /// <summary>
    /// Handles regular chat messages
    /// </summary>
    private async Task HandleChatMessageAsync(string input, CancellationToken cancellationToken)
    {
        if (!_chatSession.IsActive)
        {
            _console.MarkupLine("[red]No active session. Use /new to start a new session.[/]");
            _console.WriteLine();
            return;
        }

        try
        {
            _console.WriteLine();
            _renderer.StartStreaming();

            await foreach (var chunk in _chatSession.SendMessageStreamAsync(input, cancellationToken))
            {
                _renderer.ProcessChunk(chunk);
            }

            _renderer.CompleteStreaming();

            // Check for active git session and prompt for accept/reject
            await PromptGitSessionApprovalAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling chat message");
            _console.MarkupLine($"[red]Error: {ex.Message}[/]");
            _console.WriteLine();
        }
    }

    /// <summary>
    /// Prompts user to accept or reject git session changes if there's an active session
    /// </summary>
    private async Task PromptGitSessionApprovalAsync(CancellationToken cancellationToken)
    {
        if (_gitSessionService == null) return;

        var session = await _gitSessionService.GetCurrentSessionAsync(cancellationToken);
        if (session == null || session.ToolCallCommits.Count == 0) return;

        _console.WriteLine();
        _console.MarkupLine($"[yellow]Git Session:[/] {session.ToolCallCommits.Count} file changes detected");
        _console.MarkupLine($"[dim]Shadow branch: {session.ShadowBranch}[/]");

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]What would you like to do with these changes?[/]")
                .AddChoices(new[] {
                    "Accept and commit all changes",
                    "Reject and discard all changes",
                    "Review changes first"
                }));

        switch (choice)
        {
            case "Accept and commit all changes":
                var commitMessage = $"AI Session: Applied {session.ToolCallCommits.Count} changes";
                var accepted = await _gitSessionService.AcceptSessionAsync(commitMessage, cancellationToken);
                if (accepted)
                {
                    _console.MarkupLine($"[green]✓[/] Session accepted and committed: {session.ToolCallCommits.Count} tool calls merged");
                }
                else
                {
                    _console.MarkupLine("[red]Failed to accept session. Check logs for details.[/]");
                }
                break;

            case "Reject and discard all changes":
                var rejected = await _gitSessionService.RejectSessionAsync(cancellationToken);
                if (rejected)
                {
                    _console.MarkupLine($"[green]✓[/] Session rejected: {session.ToolCallCommits.Count} changes discarded");
                }
                else
                {
                    _console.MarkupLine("[red]Failed to reject session. Check logs for details.[/]");
                }
                break;

            case "Review changes first":
                _console.MarkupLine($"[dim]Use /session-status to review changes[/]");
                _console.MarkupLine($"[dim]Use /accept-session to accept or /reject-session to discard[/]");
                break;
        }

        _console.WriteLine();
    }
}
