using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using CodePunk.Core.Services;
using Microsoft.Extensions.Logging;

namespace CodePunk.Core.Chat;

/// <summary>
/// Manages an active interactive chat session with context and state
/// </summary>
public class InteractiveChatSession
{
    private readonly ISessionService _sessionService;
    private readonly IMessageService _messageService;
    private readonly ILLMService _llmService;
    private readonly ILogger<InteractiveChatSession> _logger;

    public Session? CurrentSession { get; private set; }
    public bool IsActive => CurrentSession != null;
    public bool IsProcessing { get; private set; }

    public InteractiveChatSession(
        ISessionService sessionService,
        IMessageService messageService,
        ILLMService llmService,
        ILogger<InteractiveChatSession> logger)
    {
        _sessionService = sessionService;
        _messageService = messageService;
        _llmService = llmService;
        _logger = logger;
    }

    /// <summary>
    /// Starts a new chat session
    /// </summary>
    public async Task<Session> StartNewSessionAsync(string title, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting new chat session: {Title}", title);
        
        CurrentSession = await _sessionService.CreateAsync(title, cancellationToken: cancellationToken);
        
        _logger.LogInformation("Created new session: {SessionId}", CurrentSession.Id);
        return CurrentSession;
    }

    /// <summary>
    /// Loads an existing session
    /// </summary>
    public async Task<bool> LoadSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Loading session: {SessionId}", sessionId);
        
        var session = await _sessionService.GetByIdAsync(sessionId, cancellationToken);
        if (session == null)
        {
            _logger.LogWarning("Session not found: {SessionId}", sessionId);
            return false;
        }

        CurrentSession = session;
        _logger.LogInformation("Loaded session: {SessionId} - {Title}", session.Id, session.Title);
        return true;
    }

    /// <summary>
    /// Sends a user message and gets AI response
    /// </summary>
    public async Task<Message> SendMessageAsync(
        string content, 
        CancellationToken cancellationToken = default)
    {
        if (!IsActive)
            throw new InvalidOperationException("No active session. Start a new session first.");

        IsProcessing = true;
        try
        {
            _logger.LogInformation("Sending message to session {SessionId}", CurrentSession!.Id);

            // Create and save user message
            var userMessage = Message.Create(
                CurrentSession.Id,
                MessageRole.User,
                [new TextPart(content)]);

            await _messageService.CreateAsync(userMessage, cancellationToken);

            // Get conversation history for AI
            var messages = await _messageService.GetBySessionAsync(CurrentSession.Id, cancellationToken);
            
            // Send to AI and get response
            var aiResponse = await _llmService.SendMessageAsync(
                messages.ToList(),
                cancellationToken);

            // Save AI response
            await _messageService.CreateAsync(aiResponse, cancellationToken);

            _logger.LogInformation("Received AI response for session {SessionId}", CurrentSession.Id);
            return aiResponse;
        }
        finally
        {
            IsProcessing = false;
        }
    }

    /// <summary>
    /// Sends a user message and streams AI response
    /// </summary>
    public async IAsyncEnumerable<ChatStreamChunk> SendMessageStreamAsync(
        string content,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!IsActive)
            throw new InvalidOperationException("No active session. Start a new session first.");

        IsProcessing = true;
        try
        {
            _logger.LogInformation("Sending streaming message to session {SessionId}", CurrentSession!.Id);

            // Create and save user message
            var userMessage = Message.Create(
                CurrentSession.Id,
                MessageRole.User,
                [new TextPart(content)]);

            await _messageService.CreateAsync(userMessage, cancellationToken);

            // Get conversation history for AI
            var messages = await _messageService.GetBySessionAsync(CurrentSession.Id, cancellationToken);
            
            // Stream AI response
            var responseBuilder = new List<MessagePart>();
            var model = string.Empty;
            var provider = string.Empty;

            await foreach (var chunk in _llmService.SendMessageStreamAsync(messages.ToList(), cancellationToken))
            {
                // Track model and provider from first chunk
                if (string.IsNullOrEmpty(model) && !string.IsNullOrEmpty(chunk.Content))
                {
                    model = "gpt-4o"; // Should come from chunk metadata when available
                    provider = "OpenAI"; // Should come from chunk metadata when available
                }

                // Build response parts
                if (chunk.Content != null)
                {
                    responseBuilder.Add(new TextPart(chunk.Content));
                }

                // Create a compatible stream chunk for the caller
                var compatibleChunk = new ChatStreamChunk
                {
                    ContentDelta = chunk.Content,
                    Model = model,
                    Provider = provider,
                    IsComplete = chunk.IsComplete
                };

                yield return compatibleChunk;
            }

            // Save complete AI response
            if (responseBuilder.Count > 0)
            {
                var completeContent = string.Join("", responseBuilder.OfType<TextPart>().Select(p => p.Content));
                var aiMessage = Message.Create(
                    CurrentSession.Id,
                    MessageRole.Assistant,
                    [new TextPart(completeContent)],
                    model,
                    provider);

                await _messageService.CreateAsync(aiMessage, cancellationToken);
            }

            _logger.LogInformation("Completed streaming response for session {SessionId}", CurrentSession.Id);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    /// <summary>
    /// Gets conversation history for the current session
    /// </summary>
    public async Task<IReadOnlyList<Message>> GetConversationHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (!IsActive)
            return Array.Empty<Message>();

        return await _messageService.GetBySessionAsync(CurrentSession!.Id, cancellationToken);
    }

    /// <summary>
    /// Clears the current session
    /// </summary>
    public void ClearSession()
    {
        _logger.LogInformation("Clearing current session");
        CurrentSession = null;
        IsProcessing = false;
    }
}
