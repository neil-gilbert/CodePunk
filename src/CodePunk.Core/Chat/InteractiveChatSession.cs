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
    /// If this is the first assistant turn in a session (one or more user messages, no assistant/tool replies),
    /// inject a lightweight, non-persisted system instruction to select a mode via `mode_*` tools.
    /// </summary>
    private void TryInjectFirstTurnModeInstruction(List<Message> currentMessages)
    {
        try
        {
            if (CurrentSession == null || currentMessages == null || currentMessages.Count == 0) return;
            var hasAssistantOrTool = currentMessages.Any(m => m.Role == MessageRole.Assistant || m.Role == MessageRole.Tool);
            if (hasAssistantOrTool) return;
            // Only inject once: add a marker by checking if we already injected (system content match)
            var alreadyInjected = currentMessages.Any(m => m.Role == MessageRole.System &&
                m.Parts.OfType<TextPart>().Any(p => p.Content.Contains("First turn mode selection", StringComparison.OrdinalIgnoreCase)));
            if (alreadyInjected) return;

            // Heuristic: only inject if first user message appears to contain actionable intent
            var lastUser = currentMessages.LastOrDefault(m => m.Role == MessageRole.User);
            var lastUserText = lastUser?.Parts.OfType<TextPart>()
                .Select(p => p.Content)
                .Aggregate(new StringBuilder(), (sb, s) => sb.AppendLine(s), sb => sb.ToString())
                ?.Trim() ?? string.Empty;
            if (!IsLikelyIntentfulFirstMessage(lastUserText))
            {
                return; // greet or low-intent: let normal chat proceed without forcing a mode
            }

            var instruction = "First turn mode selection: classify the user's request and call exactly one of these tools with concise arguments: " +
                              "mode_plan (new work/feature planning) or mode_bug (issue triage/fix). After activation, continue with realistic steps.";
            var sysText = "[First turn mode selection] " + instruction;
            var sys = Message.Create(CurrentSession.Id, MessageRole.System, new[] { new TextPart(sysText) });
            // Prepend so the provider considers it before user content
            currentMessages.Insert(0, sys);
        }
        catch
        {
            // best-effort only; never fail the chat flow
        }
    }

    private static bool IsLikelyIntentfulFirstMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        if (t.Length <= 8)
        {
            var shortLower = t.ToLowerInvariant();
            var greetings = new[] { "hi", "hello", "hey", "yo", "hola", "sup", "howdy" };
            if (greetings.Contains(shortLower)) return false;
        }
        var lower = t.ToLowerInvariant();
        // Non-intent common small talk
        var smallTalk = new[] { "how are you", "what's up", "good morning", "good evening" };
        if (smallTalk.Any(p => lower.Contains(p))) return false;

        // Positive indicators of actionable intent
        var intentVerbs = new[] { "add", "implement", "build", "create", "fix", "debug", "refactor", "migrate", "setup", "configure", "improve", "design", "plan", "feature", "change", "update", "rewrite", "optimize", "performance", "crash", "error", "exception", "test", "write tests", "bug" };
        if (intentVerbs.Any(v => lower.Contains(v))) return true;

        // Has file-like or code-like markers
        if (lower.Contains(".cs") || lower.Contains(".ts") || lower.Contains(".js") || lower.Contains(".py") || lower.Contains(".md") || lower.Contains("code") || lower.Contains("file") || lower.Contains("path "))
            return true;

        // Enough length and a question mark often implies intent
        if (t.Length > 30 && t.Contains('?')) return true;

        return false;
    }

    /// <summary>
    /// Injects an ephemeral system nudge when nearing the tool-iteration cap to guide the model
    /// towards consolidation, batching, or finalizing an answer instead of continuing to loop.
    /// </summary>
    private void TryInjectIterationGuidance(List<Message> currentMessages, int iteration, int maxIterations)
    {
        try
        {
            if (CurrentSession == null || currentMessages == null || currentMessages.Count == 0) return;
            if (maxIterations <= 0) return;

            var remaining = Math.Max(0, maxIterations - iteration + 1);
            if (remaining > 2) return; // only guide when close to cap

            // Avoid injecting duplicates for the same iteration
            var marker = $"[Loop guidance] iteration={iteration}/cap={maxIterations}";
            var already = currentMessages.Any(m => m.Role == MessageRole.System && m.Parts.OfType<TextPart>().Any(p => p.Content.Contains(marker)));
            if (already) return;

            string guidance;
            if (remaining <= 1)
            {
                guidance = "You have one tool-calling iteration remaining. Prioritize consolidating results and delivering a final answer. " +
                           "Avoid repeating reads; batch file access using read_many_files/glob if strictly necessary. " +
                           "If blocked on missing details, ask one precise clarification instead of continuing with tool calls.";
            }
            else
            {
                guidance = "You are near the tool-call limit. Consolidate actions, prefer batching (read_many_files), " +
                           "and reduce redundant operations. Aim to produce a final or near-final response.";
            }

            var sysText = marker + "\n" + guidance;
            var sys = Message.Create(CurrentSession.Id, MessageRole.System, new[] { new TextPart(sysText) });
            currentMessages.Insert(0, sys);
        }
        catch
        {
            // best-effort; never fail the chat flow
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

        // Inject first-turn mode selection instruction (ephemeral) if appropriate
        TryInjectFirstTurnModeInstruction(currentMessages);
        
        while (finalResponse == null && iteration < _options.MaxToolCallIterations)
        {
            iteration++;
            ToolIteration = iteration;
            _logger.LogInformation("Tool calling iteration {Iteration}/{MaxIterations}", 
                iteration, _options.MaxToolCallIterations);
                
            // Guidance: if we are near the iteration cap, nudge the model to consolidate
            TryInjectIterationGuidance(currentMessages, iteration, _options.MaxToolCallIterations);

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
        
        while (iteration < _options.MaxToolCallIterations)
        {
            iteration++;
            ToolIteration = iteration;
            _eventStream.TryWrite(new ChatSessionEvent(ChatSessionEventType.ToolIterationStart, CurrentSession!.Id, iteration));

            _logger.LogInformation("Tool calling iteration {Iteration}/{MaxIterations}", 
                iteration, _options.MaxToolCallIterations);
            
            var responseContent = new StringBuilder();
            var toolCalls = new List<ToolCallPart>();
            var model = _options.DefaultModel;
            var provider = _options.DefaultProvider;

            // Inject first-turn mode selection instruction (ephemeral) if appropriate
            TryInjectFirstTurnModeInstruction(currentMessages);

            // Guidance: if we are near the iteration cap, nudge the model to consolidate
            TryInjectIterationGuidance(currentMessages, iteration, _options.MaxToolCallIterations);

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
