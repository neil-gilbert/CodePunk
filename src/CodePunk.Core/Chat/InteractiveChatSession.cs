using System.Text;
using System.Linq;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Extensions;
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
    private readonly IChatSessionEventStream _eventStream;
    private readonly IChatSessionOptions _options;

    public Session? CurrentSession { get; private set; }
    public bool IsActive => CurrentSession != null;
    public bool IsProcessing { get; private set; }
    public string DefaultModel => _options.DefaultModel;
    public string DefaultProvider => _options.DefaultProvider;
    public int ToolIteration { get; private set; }
    public bool IsToolLoopActive => ToolIteration > 0;
    public int MaxToolIterations => _options.MaxToolCallIterations;
    public long AccumulatedPromptTokens { get; private set; }
    public long AccumulatedCompletionTokens { get; private set; }
    public decimal AccumulatedCost { get; private set; }
    public IChatSessionEventStream Events => _eventStream;

    public InteractiveChatSession(
        ISessionService sessionService,
        IMessageService messageService,
        ILLMService llmService,
        IToolService toolService,
        ILogger<InteractiveChatSession> logger,
        IChatSessionEventStream? eventStream = null,
        IChatSessionOptions? options = null)
    {
        _sessionService = sessionService;
        _messageService = messageService;
        _llmService = llmService;
        _toolService = toolService;
        _logger = logger;
        _eventStream = eventStream ?? new ChatSessionEventStream();
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
            // Propagate to LLM service so request construction respects user selection
            try { _llmService.SetSessionDefaults(opts.DefaultProvider, opts.DefaultModel); } catch { }
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
    _eventStream.TryWrite(new ChatSessionEvent(ChatSessionEventType.MessageStart, CurrentSession!.Id));
        await Task.Yield(); // allow observers to see IsProcessing=true before heavy work
        try
        {
            _logger.LogInformation("Sending message to session {SessionId}", CurrentSession!.Id);

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
            _eventStream.TryWrite(new ChatSessionEvent(ChatSessionEventType.MessageComplete, CurrentSession?.Id, IsFinal: true));
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
        var toolCallHistory = new Dictionary<string, int>();
        var repeatedToolIterations = 0;
        var consecutiveErrorIterations = 0;
        
        while (finalResponse == null && iteration < _options.MaxToolCallIterations)
        {
            iteration++;
            ToolIteration = iteration;
            _logger.LogInformation("Tool iteration {Iteration}/{MaxIterations} (history entries: {HistoryCount})",
                iteration, _options.MaxToolCallIterations, toolCallHistory.Count);
                
            // Get AI response
            var (content, toolCalls) = await AIResponseProcessor.ProcessStreamingResponseAsync(
                _llmService.SendMessageStreamAsync(currentMessages, cancellationToken), 
                cancellationToken).ConfigureAwait(false);

            // Create and save AI response
            var aiResponse = AIResponseProcessor.CreateAIMessage(
                CurrentSession!.Id, content, toolCalls, _options.DefaultModel, _options.DefaultProvider);
                
            await _messageService.CreateAsync(aiResponse, cancellationToken).ConfigureAwait(false);
            currentMessages.Add(aiResponse);
            // NOTE: Non-streaming path currently lacks precise usage; could approximate here later if needed.

            // If no tool calls, this is the final response
            if (toolCalls.Count == 0)
            {
                finalResponse = aiResponse;
                break;
            }

            if (_options.MaxToolCallsPerIteration > 0 && toolCalls.Count > _options.MaxToolCallsPerIteration)
            {
                finalResponse = await CreateGuardrailMessageAsync(
                        $"The assistant requested {toolCalls.Count} tool commands at once. Split the work into batches of {_options.MaxToolCallsPerIteration} or fewer before continuing.",
                        cancellationToken)
                    .ConfigureAwait(false);
                currentMessages.Add(finalResponse);
                _eventStream.TryWrite(new ChatSessionEvent(ChatSessionEventType.ToolLoopAborted, CurrentSession?.Id, iteration));
                break;
            }

            _logger.LogInformation("AI requested {ToolCount} tool call(s): {ToolSummary}",
                toolCalls.Count, DescribeToolCalls(toolCalls));

            var repeatedThisIteration = RegisterToolCalls(toolCalls, toolCallHistory);
            if (repeatedThisIteration)
            {
                repeatedToolIterations++;
                _logger.LogWarning("Repeated tool commands detected on iteration {Iteration}; streak={Streak}",
                    iteration, repeatedToolIterations);
            }
            else
            {
                repeatedToolIterations = 0;
            }

            if (_options.MaxRepeatedToolCalls > 0 && repeatedToolIterations >= _options.MaxRepeatedToolCalls)
            {
                finalResponse = await CreateGuardrailMessageAsync(
                        "Stopped tool execution because the assistant kept issuing the same command. Adjust the plan or run the command manually.",
                        cancellationToken)
                    .ConfigureAwait(false);
                currentMessages.Add(finalResponse);
                _eventStream.TryWrite(new ChatSessionEvent(ChatSessionEventType.ToolLoopAborted, CurrentSession?.Id, iteration));
                break;
            }

            // Execute tool calls and create tool result message
            var (toolResultParts, userCancelled) = await ToolExecutionHelper.ExecuteToolCallsAsync(
                toolCalls, _toolService, _logger, cancellationToken).ConfigureAwait(false);

            var toolResultMessage = AIResponseProcessor.CreateToolResultsMessage(CurrentSession.Id, toolResultParts);
            await _messageService.CreateAsync(toolResultMessage, cancellationToken).ConfigureAwait(false);
            currentMessages.Add(toolResultMessage);

            // If user cancelled, stop the tool loop and return immediately
            if (userCancelled)
            {
                _logger.LogInformation("User cancelled operation, stopping tool execution loop");
                ToolIteration = 0; // reset after cancellation
                return finalResponse ?? Message.Create(
                    CurrentSession.Id,
                    MessageRole.Assistant,
                    [new TextPart("Operation cancelled by user.")],
                    _options.DefaultModel,
                    _options.DefaultProvider);
            }

            var allErrors = toolResultParts.Count > 0 && toolResultParts.All(p => p.IsError);
            if (allErrors)
            {
                consecutiveErrorIterations++;
                _logger.LogWarning("All tool calls failed on iteration {Iteration}; consecutive failures={Failures}",
                    iteration, consecutiveErrorIterations);
            }
            else
            {
                consecutiveErrorIterations = 0;
            }

            if (_options.MaxConsecutiveToolErrors > 0 && consecutiveErrorIterations >= _options.MaxConsecutiveToolErrors)
            {
                finalResponse = await CreateGuardrailMessageAsync(
                        "Halting tool loop after repeated tool failures. Inspect the previous tool output and revise the request before continuing.",
                        cancellationToken)
                    .ConfigureAwait(false);
                currentMessages.Add(finalResponse);
                _eventStream.TryWrite(new ChatSessionEvent(ChatSessionEventType.ToolLoopAborted, CurrentSession?.Id, iteration));
                break;
            }
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

    ToolIteration = 0; // reset after loop completes
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
        _eventStream.TryWrite(new ChatSessionEvent(ChatSessionEventType.MessageStart, CurrentSession!.Id));

        await Task.Yield(); // allow observers to see IsProcessing=true before streaming work

        try
        {
            _logger.LogInformation("Sending streaming message to session {SessionId}", CurrentSession!.Id);

            var userMessage = Message.Create(
                CurrentSession.Id,
                MessageRole.User,
                [new TextPart(content)]);

            await _messageService.CreateAsync(userMessage, cancellationToken).ConfigureAwait(false);

            
            var messages = await _messageService.GetBySessionAsync(CurrentSession.Id, cancellationToken)
                .ConfigureAwait(false);

            await foreach (var chunk in ProcessConversationStreamAsync(messages.ToList(), cancellationToken))
            {
                yield return chunk;
            }

            _logger.LogInformation("Completed streaming response for session {SessionId}", CurrentSession.Id);
        }
        finally
        {
            IsProcessing = false;
            _eventStream.TryWrite(new ChatSessionEvent(ChatSessionEventType.MessageComplete, CurrentSession?.Id, IsFinal: true));
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
        var toolCallHistory = new Dictionary<string, int>();
        var repeatedToolIterations = 0;
        var consecutiveErrorIterations = 0;

        while (iteration < _options.MaxToolCallIterations)
        {
            iteration++;
            ToolIteration = iteration;
            _eventStream.TryWrite(new ChatSessionEvent(ChatSessionEventType.ToolIterationStart, CurrentSession!.Id, iteration));

            _logger.LogInformation("Tool calling iteration {Iteration}/{MaxIterations} (history entries: {HistoryCount})", 
                iteration, _options.MaxToolCallIterations, toolCallHistory.Count);
            
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
                    IsComplete = chunk.IsComplete,
                    InputTokens = chunk.Usage?.InputTokens,
                    OutputTokens = chunk.Usage?.OutputTokens,
                    EstimatedCost = chunk.Usage?.EstimatedCost
                };

                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    _eventStream.TryWrite(new ChatSessionEvent(ChatSessionEventType.StreamDelta, CurrentSession!.Id, ToolIteration, chunk.Content, chunk.IsComplete));
                }
                yield return compatibleChunk;

                if (chunk.IsComplete && chunk.Usage != null)
                {
                    // Update accumulated session usage
                    AccumulatedPromptTokens += chunk.Usage.InputTokens;
                    AccumulatedCompletionTokens += chunk.Usage.OutputTokens;
                    AccumulatedCost += chunk.Usage.EstimatedCost;

                    // Persist to session if available
                    if (CurrentSession != null)
                    {
                        CurrentSession = CurrentSession with
                        {
                            PromptTokens = AccumulatedPromptTokens,
                            CompletionTokens = AccumulatedCompletionTokens,
                            Cost = AccumulatedCost
                        };
                        try { await _sessionService.UpdateAsync(CurrentSession, cancellationToken).ConfigureAwait(false); }
                        catch (Exception ex) { _logger.LogWarning(ex, "Failed to persist session usage update"); }
                    }
                }
            }

            // Create and save AI response
            var aiResponse = AIResponseProcessor.CreateAIMessage(
                CurrentSession!.Id, responseContent.ToString(), toolCalls, model, provider);
                
            await _messageService.CreateAsync(aiResponse, cancellationToken).ConfigureAwait(false);
            currentMessages.Add(aiResponse);

            // If no tool calls, this is the final response
            if (toolCalls.Count == 0)
            {
                ToolIteration = 0; // final response reached
                _eventStream.TryWrite(new ChatSessionEvent(ChatSessionEventType.ToolIterationEnd, CurrentSession!.Id, iteration));
                yield break;
            }

            _logger.LogInformation("AI requested {ToolCount} tool call(s): {ToolSummary}",
                toolCalls.Count, DescribeToolCalls(toolCalls));

            var repeatedThisIteration = RegisterToolCalls(toolCalls, toolCallHistory);
            if (repeatedThisIteration)
            {
                repeatedToolIterations++;
                _logger.LogWarning("Repeated tool commands detected on iteration {Iteration}; streak={Streak}",
                    iteration, repeatedToolIterations);
            }
            else
            {
                repeatedToolIterations = 0;
            }

            if (_options.MaxRepeatedToolCalls > 0 && repeatedToolIterations >= _options.MaxRepeatedToolCalls)
            {
                var guardMessage = await CreateGuardrailMessageAsync(
                        "Stopped tool execution because the assistant kept issuing the same command. Adjust the plan or run the command manually.",
                        cancellationToken)
                    .ConfigureAwait(false);
                currentMessages.Add(guardMessage);
                var guardText = guardMessage.Parts.OfType<TextPart>().FirstOrDefault()?.Content ?? string.Empty;
                _eventStream.TryWrite(new ChatSessionEvent(ChatSessionEventType.ToolLoopAborted, CurrentSession?.Id, iteration));
                ToolIteration = 0;
                yield return new ChatStreamChunk
                {
                    ContentDelta = guardText,
                    Model = model,
                    Provider = provider,
                    IsComplete = true
                };
                yield break;
            }

            // Execute tool calls with streaming status updates
            var (toolResultParts, statusMessages, userCancelled) = await ToolExecutionHelper.ExecuteToolCallsWithStatusAsync(
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

            // If user cancelled, stop the tool loop and return immediately
            if (userCancelled)
            {
                _logger.LogInformation("User cancelled operation, stopping streaming tool execution loop");
                ToolIteration = 0; // reset after cancellation

                yield return new ChatStreamChunk
                {
                    ContentDelta = "Operation cancelled by user.",
                    Model = model,
                    Provider = provider,
                    IsComplete = true
                };

                _eventStream.TryWrite(new ChatSessionEvent(ChatSessionEventType.ToolIterationEnd, CurrentSession!.Id, iteration));
                yield break;
            }

            var allErrors = toolResultParts.Count > 0 && toolResultParts.All(p => p.IsError);
            if (allErrors)
            {
                consecutiveErrorIterations++;
                _logger.LogWarning("All tool calls failed on iteration {Iteration}; consecutive failures={Failures}",
                    iteration, consecutiveErrorIterations);
            }
            else
            {
                consecutiveErrorIterations = 0;
            }

            if (_options.MaxConsecutiveToolErrors > 0 && consecutiveErrorIterations >= _options.MaxConsecutiveToolErrors)
            {
                var guardMessage = await CreateGuardrailMessageAsync(
                        "Halting tool loop after repeated tool failures. Inspect the previous tool output and revise the request before continuing.",
                        cancellationToken)
                    .ConfigureAwait(false);
                currentMessages.Add(guardMessage);
                var guardText = guardMessage.Parts.OfType<TextPart>().FirstOrDefault()?.Content ?? string.Empty;
                _eventStream.TryWrite(new ChatSessionEvent(ChatSessionEventType.ToolLoopAborted, CurrentSession?.Id, iteration));
                ToolIteration = 0;
                yield return new ChatStreamChunk
                {
                    ContentDelta = guardText,
                    Model = model,
                    Provider = provider,
                    IsComplete = true
                };
                yield break;
            }

            _eventStream.TryWrite(new ChatSessionEvent(ChatSessionEventType.ToolIterationEnd, CurrentSession!.Id, iteration));
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

        ToolIteration = 0;

        if (iteration >= _options.MaxToolCallIterations)
        {
            _eventStream.TryWrite(new ChatSessionEvent(ChatSessionEventType.ToolLoopExceeded, CurrentSession?.Id, iteration));
        }
    }

    private static bool RegisterToolCalls(IEnumerable<ToolCallPart> toolCalls, IDictionary<string, int> history)
    {
        var repeated = false;
        foreach (var toolCall in toolCalls)
        {
            var signature = toolCall.GetStableSignature();
            if (history.TryGetValue(signature, out var count))
            {
                history[signature] = count + 1;
                repeated = true;
            }
            else
            {
                history[signature] = 1;
            }
        }

        return repeated;
    }

    private static string DescribeToolCalls(IEnumerable<ToolCallPart> toolCalls)
    {
        var names = toolCalls.Select(call => call.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
        return names.Length > 0 ? string.Join(", ", names) : "none";
    }

    private async Task<Message> CreateGuardrailMessageAsync(string content, CancellationToken cancellationToken)
    {
        var guardMessage = Message.Create(
            CurrentSession!.Id,
            MessageRole.Assistant,
            [new TextPart(content)],
            _options.DefaultModel,
            _options.DefaultProvider);

        await _messageService.CreateAsync(guardMessage, cancellationToken).ConfigureAwait(false);
        return guardMessage;
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
        _eventStream.TryWrite(new ChatSessionEvent(ChatSessionEventType.SessionCleared));
    }
}
