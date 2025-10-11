using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CodePunk.Console.Commands;
using CodePunk.Console.Rendering;
using CodePunk.Console.Rendering.Animations;
using CodePunk.Core.Chat;
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
    private readonly ILogger<InteractiveChatLoop> _logger;
    private readonly ConsoleAnimationOptions _animationOptions;

    private bool _shouldExit;
    private bool _shouldCreateNewSession;

    public InteractiveChatLoop(
        InteractiveChatSession chatSession,
        CommandProcessor commandProcessor,
        StreamingResponseRenderer renderer,
        IAnsiConsole console,
        ILogger<InteractiveChatLoop> logger,
        ConsoleAnimationOptions animationOptions)
    {
        _chatSession = chatSession;
        _commandProcessor = commandProcessor;
        _renderer = renderer;
        _console = console;
        _logger = logger;
        _animationOptions = animationOptions;
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
            if (ToolStatusSerializer.TryDeserialize(chunk.ContentDelta, out var status) && status != null)
            {
                sb.Append(status.IsError ? "❌" : "✅").Append(' ').AppendLine(status.ToolName);
                if (!string.IsNullOrEmpty(status.FilePath))
                    sb.AppendLine(status.FilePath);
                if (!string.IsNullOrEmpty(status.Preview))
                    sb.AppendLine(status.Preview);
                if (status.IsTruncated)
                    sb.AppendLine($"… showing first {status.MaxLines} lines of {status.OriginalLineCount}");
            }
            else if (!string.IsNullOrEmpty(chunk.ContentDelta))
            {
                sb.Append(chunk.ContentDelta);
            }
        }
        _renderer.CompleteStreaming();
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
            if (_animationOptions.EnableStatusAnimation)
            {
                await _console.Status()
                    .Spinner(ThinkingSpinnerFactory.Spinner)
                    .SpinnerStyle(new Style(Color.Aqua))
                    .StartAsync(async ctx =>
                    {
                        ctx.Status("Thinking…");
                        using var subscription = new StatusEventSubscription(ctx, _chatSession.Events, _chatSession.CurrentSession?.Id, cancellationToken);
                        await ExecuteChatMessageCoreAsync(input, cancellationToken);
                    });
            }
            else
            {
                await ExecuteChatMessageCoreAsync(input, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling chat message");
            _console.MarkupLine($"[red]Error: {ex.Message}[/]");
            _console.WriteLine();
        }
    }

    private async Task ExecuteChatMessageCoreAsync(string input, CancellationToken cancellationToken)
    {
        _console.WriteLine();
        _renderer.StartStreaming();

        await foreach (var chunk in _chatSession.SendMessageStreamAsync(input, cancellationToken))
        {
            _renderer.ProcessChunk(chunk);
        }

        _renderer.CompleteStreaming();
    }

    private sealed class StatusEventSubscription : IDisposable
    {
        private readonly StatusContext _context;
        private readonly IChatSessionEventStream _events;
        private readonly string? _sessionId;
        private readonly CancellationTokenSource _cts;
        private readonly Task _worker;

        public StatusEventSubscription(StatusContext context, IChatSessionEventStream events, string? sessionId, CancellationToken externalToken)
        {
            _context = context;
            _events = events;
            _sessionId = sessionId;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            _worker = Task.Run(() => PumpAsync(_cts.Token), _cts.Token);
        }

        private async Task PumpAsync(CancellationToken token)
        {
            await foreach (var evt in _events.Reader.ReadAllAsync(token))
            {
                if (!string.IsNullOrEmpty(_sessionId) && !string.IsNullOrEmpty(evt.SessionId) && !string.Equals(evt.SessionId, _sessionId, StringComparison.Ordinal))
                {
                    continue;
                }

                switch (evt.Type)
                {
                    case ChatSessionEventType.MessageStart:
                        _context.Status("Thinking…");
                        break;
                    case ChatSessionEventType.ToolIterationStart:
                        var iteration = evt.Iteration ?? 0;
                        _context.Status(iteration > 0
                            ? $"Executing tools (pass {iteration})"
                            : "Executing tools…");
                        break;
                    case ChatSessionEventType.ToolIterationEnd:
                        _context.Status("Thinking…");
                        break;
                    case ChatSessionEventType.ToolLoopAborted:
                        _context.Status("Tool loop interrupted");
                        break;
                    case ChatSessionEventType.ToolLoopExceeded:
                        _context.Status("Max tool iterations reached");
                        break;
                }

                if (evt.Type == ChatSessionEventType.MessageComplete && evt.IsFinal == true)
                {
                    return;
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _worker.Wait(); }
            catch { }
            _cts.Dispose();
        }
    }
}
