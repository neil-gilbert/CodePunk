using System.Text;
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
    private readonly IToolService _toolService;
    private readonly ILogger<InteractiveChatSession> _logger;
    private readonly IChatSessionOptions _options;

    public Session? CurrentSession { get; private set; }
    public bool IsActive => CurrentSession != null;
    public bool IsProcessing { get; private set; }

    public InteractiveChatSession(
        ISessionService sessionService,
        IMessageService messageService,
        ILLMService llmService,
        IToolService toolService,
        ILogger<InteractiveChatSession> logger,
        IChatSessionOptions? options = null)
    {
        _sessionService = sessionService;
        _messageService = messageService;
        _llmService = llmService;
        _toolService = toolService;
        _logger = logger;
        _options = options ?? new ChatSessionOptions();
    }

    /// <summary>
    /// Update default provider/model used for subsequent AI calls.
    /// </summary>
    public void UpdateDefaults(string? provider, string? model)
    {
        if (_options is ChatSessionOptions opts)
        {
            if (!string.IsNullOrWhiteSpace(provider)) opts.DefaultProvider = provider.Trim();
            if (!string.IsNullOrWhiteSpace(model)) opts.DefaultModel = model.Trim();
            _logger.LogDebug("Chat defaults updated to provider={Provider} model={Model}", opts.DefaultProvider, opts.DefaultModel);
        }
    }

    /// <summary>
    /// Starts a new chat session
    /// </summary>
    public async Task<Session> StartNewSessionAsync(string title, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting new chat session: {Title}", title);
        
        CurrentSession = await _sessionService.CreateAsync(title, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        
        _logger.LogInformation("Created new session: {SessionId}", CurrentSession.Id);
        return CurrentSession;
    }

    /// <summary>
    /// Loads an existing session
    /// </summary>
    public async Task<bool> LoadSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Loading session: {SessionId}", sessionId);
        
        var session = await _sessionService.GetByIdAsync(sessionId, cancellationToken)
            .ConfigureAwait(false);
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
            // Yield once so external observers (tests/UI) can detect processing state before work completes.
            await Task.Yield();
            // Small delay to make IsProcessing reliably observable in fast unit tests
            await Task.Delay(1, cancellationToken);
            _logger.LogInformation("Sending message to session {SessionId}", CurrentSession!.Id);

            // Create and save user message
            var userMessage = Message.Create(
                CurrentSession.Id,
                MessageRole.User,
                [new TextPart(content)]);

            await _messageService.CreateAsync(userMessage, cancellationToken).ConfigureAwait(false);

            // Get conversation history for AI
            var messages = await _messageService.GetBySessionAsync(CurrentSession.Id, cancellationToken)
                .ConfigureAwait(false);
            
            // Process conversation with tool calling loop
            return await ProcessConversationAsync(messages.ToList(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    /// <summary>
    /// Processes conversation with tool calling loop
    /// </summary>
    private async Task<Message> ProcessConversationAsync(
        List<Message> currentMessages, 
        CancellationToken cancellationToken)
    {
        Message? finalResponse = null;
        var iteration = 0;
        
        while (finalResponse == null && iteration < _options.MaxToolCallIterations)
        {
            iteration++;
            _logger.LogInformation("Tool calling iteration {Iteration}/{MaxIterations}", 
                iteration, _options.MaxToolCallIterations);
                
            // Get AI response
            var (content, toolCalls) = await AIResponseProcessor.ProcessStreamingResponseAsync(
                _llmService.SendMessageStreamAsync(currentMessages, cancellationToken), 
                cancellationToken).ConfigureAwait(false);

            // Create and save AI response
            var aiResponse = AIResponseProcessor.CreateAIMessage(
                CurrentSession!.Id, content, toolCalls, _options.DefaultModel, _options.DefaultProvider);
                
            await _messageService.CreateAsync(aiResponse, cancellationToken).ConfigureAwait(false);
            currentMessages.Add(aiResponse);

            // If no tool calls, this is the final response
            if (toolCalls.Count == 0)
            {
                finalResponse = aiResponse;
                break;
            }

            // Execute tool calls and create tool result message
            var toolResultParts = await ToolExecutionHelper.ExecuteToolCallsAsync(
                toolCalls, _toolService, _logger, cancellationToken).ConfigureAwait(false);

            var toolResultMessage = AIResponseProcessor.CreateToolResultsMessage(CurrentSession.Id, toolResultParts);
            await _messageService.CreateAsync(toolResultMessage, cancellationToken).ConfigureAwait(false);
            currentMessages.Add(toolResultMessage);
        }

        // Handle case where we hit max iterations without a final response
        if (finalResponse == null)
        {
            _logger.LogWarning("Tool calling loop exceeded maximum iterations ({MaxIterations}), creating fallback response", 
                _options.MaxToolCallIterations);
            
            var fallbackMessage = AIResponseProcessor.CreateFallbackMessage(
                CurrentSession!.Id, _options.DefaultModel, _options.DefaultProvider);
            
            await _messageService.CreateAsync(fallbackMessage, cancellationToken).ConfigureAwait(false);
            finalResponse = fallbackMessage;
        }

        _logger.LogInformation("Received final AI response for session {SessionId}", CurrentSession!.Id);
        return finalResponse;
    }

    /// <summary>
    /// Sends a user message and streams AI response with tool execution
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
            // Yield control to surface IsProcessing state to observers before intensive work.
            await Task.Yield();
            // Small delay to make IsProcessing reliably observable in fast unit tests
            await Task.Delay(1, cancellationToken);
            _logger.LogInformation("Sending streaming message to session {SessionId}", CurrentSession!.Id);

            // Create and save user message
            var userMessage = Message.Create(
                CurrentSession.Id,
                MessageRole.User,
                [new TextPart(content)]);

            await _messageService.CreateAsync(userMessage, cancellationToken).ConfigureAwait(false);

            // Get conversation history for AI
            var messages = await _messageService.GetBySessionAsync(CurrentSession.Id, cancellationToken)
                .ConfigureAwait(false);
            
            // Process conversation with streaming
            await foreach (var chunk in ProcessConversationStreamAsync(messages.ToList(), cancellationToken))
            {
                yield return chunk;
            }

            _logger.LogInformation("Completed streaming response for session {SessionId}", CurrentSession.Id);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    /// <summary>
    /// Processes conversation with streaming tool calling loop
    /// </summary>
    private async IAsyncEnumerable<ChatStreamChunk> ProcessConversationStreamAsync(
        List<Message> currentMessages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var iteration = 0;
        
        while (iteration < _options.MaxToolCallIterations)
        {
            iteration++;
            _logger.LogInformation("Tool calling iteration {Iteration}/{MaxIterations}", 
                iteration, _options.MaxToolCallIterations);
            
            // Stream AI response and collect content/tool calls in real-time
            var responseContent = new StringBuilder();
            var toolCalls = new List<ToolCallPart>();
            var model = _options.DefaultModel;
            var provider = _options.DefaultProvider;

            await foreach (var chunk in _llmService.SendMessageStreamAsync(currentMessages, cancellationToken))
            {
                // Track model and provider from first chunk
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    model = _options.DefaultModel;
                    provider = _options.DefaultProvider;
                }

                if (chunk.Content != null)
                {
                    responseContent.Append(chunk.Content);
                }
                
                if (chunk.ToolCall != null)
                {
                    toolCalls.Add(new ToolCallPart(chunk.ToolCall.Id, chunk.ToolCall.Name, chunk.ToolCall.Arguments));
                }

                // Stream the content to the caller in real-time (preserve original IsComplete)
                var compatibleChunk = new ChatStreamChunk
                {
                    ContentDelta = chunk.Content,
                    Model = model,
                    Provider = provider,
                    IsComplete = chunk.IsComplete
                };

                yield return compatibleChunk;
            }

            // Create and save AI response
            var aiResponse = AIResponseProcessor.CreateAIMessage(
                CurrentSession!.Id, responseContent.ToString(), toolCalls, model, provider);
                
            await _messageService.CreateAsync(aiResponse, cancellationToken).ConfigureAwait(false);
            currentMessages.Add(aiResponse);

            // If no tool calls, this is the final response
            if (toolCalls.Count == 0)
            {
                yield break;
            }

            // Execute tool calls with streaming status updates
            var (toolResultParts, statusMessages) = await ToolExecutionHelper.ExecuteToolCallsWithStatusAsync(
                toolCalls, _toolService, _logger, cancellationToken).ConfigureAwait(false);

            // Stream tool execution status messages
            foreach (var statusMessage in statusMessages)
            {
                yield return new ChatStreamChunk
                {
                    ContentDelta = statusMessage,
                    Model = model,
                    Provider = provider,
                    IsComplete = false
                };
            }

            // Create and save tool results message
            var toolResultMessage = AIResponseProcessor.CreateToolResultsMessage(CurrentSession.Id, toolResultParts);
            await _messageService.CreateAsync(toolResultMessage, cancellationToken).ConfigureAwait(false);
            currentMessages.Add(toolResultMessage);
        }

        // Handle case where we hit max iterations without a final response
        if (iteration >= _options.MaxToolCallIterations)
        {
            _logger.LogWarning("Tool calling loop exceeded maximum iterations ({MaxIterations}), creating fallback response", 
                _options.MaxToolCallIterations);
            
            var fallbackMessage = AIResponseProcessor.CreateFallbackMessage(
                CurrentSession!.Id, _options.DefaultModel, _options.DefaultProvider);
            
            await _messageService.CreateAsync(fallbackMessage, cancellationToken).ConfigureAwait(false);
            
            // Stream the fallback message
            yield return new ChatStreamChunk
            {
                ContentDelta = fallbackMessage.Parts.OfType<TextPart>().FirstOrDefault()?.Content ?? "",
                Model = _options.DefaultModel,
                Provider = _options.DefaultProvider,
                IsComplete = true
            };
        }
    }

    /// <summary>
    /// Gets conversation history for the current session
    /// </summary>
    public async Task<IReadOnlyList<Message>> GetConversationHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (!IsActive)
            return Array.Empty<Message>();

        return await _messageService.GetBySessionAsync(CurrentSession!.Id, cancellationToken)
            .ConfigureAwait(false);
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
