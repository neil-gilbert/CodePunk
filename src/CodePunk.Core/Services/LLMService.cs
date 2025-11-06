using System.Runtime.CompilerServices;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Caching;
using CodePunk.Core.Models;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace CodePunk.Core.Services;

/// <summary>
/// Service for managing LLM providers and routing requests
/// </summary>
public interface ILLMService
{
    IReadOnlyList<ILLMProvider> GetProviders();
    ILLMProvider? GetProvider(string name);
    ILLMProvider GetDefaultProvider();
    Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default);
    Task<LLMResponse> SendAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<LLMStreamChunk> StreamAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default);
    Task<Message> SendMessageAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default);
    IAsyncEnumerable<LLMStreamChunk> SendMessageStreamAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default);
    void SetSessionDefaults(string? providerName, string? modelId);
    Task<int> CountTokensAsync(LLMRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of LLM service
/// </summary>
public class LLMService : ILLMService
{
    private readonly ILLMProviderFactory _providerFactory;
    private readonly IPromptProvider _promptProvider;
    private readonly IToolService _toolService;
    private readonly IPromptCache? _promptCache;
    private readonly PromptCacheOptions _cacheOptions;
    private readonly ILogger<LLMService>? _logger;
    private string? _overrideProvider;
    private string? _overrideModel;

    public LLMService(
        ILLMProviderFactory providerFactory,
        IPromptProvider promptProvider,
        IToolService toolService,
        IPromptCache? promptCache = null,
        IOptions<PromptCacheOptions>? cacheOptions = null,
        ILogger<LLMService>? logger = null)
    {
        _providerFactory = providerFactory;
        _promptProvider = promptProvider;
        _toolService = toolService;
        _promptCache = promptCache;
        _cacheOptions = cacheOptions?.Value ?? new PromptCacheOptions();
        _logger = logger;
    }

    public IReadOnlyList<ILLMProvider> GetProviders()
    {
        var providers = new List<ILLMProvider>();
        foreach (var providerName in _providerFactory.GetAvailableProviders())
        {
            try { providers.Add(_providerFactory.GetProvider(providerName)); } catch { }
        }
        return providers;
    }

    public ILLMProvider? GetProvider(string name)
    {
        try { return _providerFactory.GetProvider(name); } catch { return null; }
    }

    public ILLMProvider GetDefaultProvider() => _providerFactory.GetProvider();

    public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default) =>
        SendWithCacheAsync(ResolveProvider(), request, cancellationToken);

    public Task<LLMResponse> SendAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default)
    {
        var provider = _providerFactory.GetProvider(providerName);
        return SendWithCacheAsync(provider, request, cancellationToken);
    }

    public IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, CancellationToken cancellationToken = default) =>
        StreamWithCacheAsync(ResolveProvider(), request, cancellationToken);

    public IAsyncEnumerable<LLMStreamChunk> StreamAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default)
    {
        var provider = _providerFactory.GetProvider(providerName);
        return StreamWithCacheAsync(provider, request, cancellationToken);
    }

    public async Task<Message> SendMessageAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider();
        var request = ConvertMessagesToRequest(conversationHistory, provider.Name);
        var response = await SendWithCacheAsync(provider, request, cancellationToken);
        return ConvertResponseToMessage(response, conversationHistory.Last().SessionId, provider.Name);
    }

    public IAsyncEnumerable<LLMStreamChunk> SendMessageStreamAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider();
        var request = ConvertMessagesToRequest(conversationHistory, provider.Name);
        return StreamWithCacheAsync(provider, request, cancellationToken);
    }

    public void SetSessionDefaults(string? providerName, string? modelId)
    {
        if (!string.IsNullOrWhiteSpace(providerName)) _overrideProvider = providerName.Trim();
        if (!string.IsNullOrWhiteSpace(modelId)) _overrideModel = modelId.Trim();
    }

    public async Task<int> CountTokensAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider();
        if (provider is CodePunk.Core.Providers.Anthropic.AnthropicProvider anthropic)
        {
            return await anthropic.CountTokensAsync(request, cancellationToken).ConfigureAwait(false);
        }
        return 0;
    }

    private ILLMProvider ResolveProvider()
    {
        if (!string.IsNullOrWhiteSpace(_overrideProvider))
        {
            try { return _providerFactory.GetProvider(_overrideProvider); } catch { }
        }
        return GetDefaultProvider();
    }

    private LLMRequest ConvertMessagesToRequest(IList<Message> messages, string providerName)
    {
        var systemPrompt = _promptProvider.GetSystemPrompt(providerName, PromptType.Coder);

        var workingDir = Directory.GetCurrentDirectory();
        systemPrompt += $"\n\nWorking directory: {workingDir}\n";

        var provider = _providerFactory.GetProvider(providerName);
        var modelId = !string.IsNullOrWhiteSpace(_overrideModel) && provider.Models.Any(m => m.Id == _overrideModel)
            ? _overrideModel!
            : provider.Models.FirstOrDefault()?.Id ?? "gpt-4o";

        var msgs = messages.ToList();
        // Token-aware truncation: trim history to fit model context with headroom for completion
        try
        {
            var model = provider.Models.FirstOrDefault(m => m.Id == modelId);
            var contextWindow = model?.ContextWindow > 0 ? model!.ContextWindow : 4096;
            msgs = TruncateMessagesToBudget(msgs, systemPrompt, contextWindow);
        }
        catch { }
        var hasAssistantOrTool = msgs.Any(m => m.Role == MessageRole.Assistant || m.Role == MessageRole.Tool);
        var allTools = _toolService.GetLLMTools();
        IReadOnlyList<LLMTool>? toolsToSend = allTools;
        if (!hasAssistantOrTool)
        {
            // First meaningful turn: restrict to mode tools to reduce token usage
            toolsToSend = allTools.Where(t => string.Equals(t.Name, "mode_plan", StringComparison.OrdinalIgnoreCase)
                                           || string.Equals(t.Name, "mode_bug", StringComparison.OrdinalIgnoreCase))
                                  .ToList();
        }
        

        var request = new LLMRequest
        {
            ModelId = modelId,
            Messages = msgs.AsReadOnly(),
            Tools = toolsToSend,
            MaxTokens = 4096,
            Temperature = 0.2,
            SystemPrompt = systemPrompt,
            ToolChoice = !hasAssistantOrTool ? "required" : null
        };
        return request;
    }

    private static List<Message> TruncateMessagesToBudget(List<Message> allMessages, string? systemPrompt, int contextWindow)
    {
        // Reserve headroom for completion and tool/status chatter
        var generationHeadroom = Math.Max(512, (int)Math.Round(contextWindow * 0.25));
        var promptBudget = Math.Max(1024, contextWindow - generationHeadroom);

        var messages = new List<Message>(allMessages);
        if (messages.Count == 0) return messages;

        while (EstimateTokens(messages, systemPrompt) > promptBudget && messages.Count > 1)
        {
            // Remove the earliest non-system message. If followed by a Tool message, remove both together.
            var idx = messages.FindIndex(m => m.Role != MessageRole.System);
            if (idx < 0) break;
            if (idx + 1 < messages.Count && messages[idx + 1].Role == MessageRole.Tool)
            {
                messages.RemoveAt(idx + 1);
            }
            messages.RemoveAt(idx);
        }

        return messages;
    }

    private static int EstimateTokens(IEnumerable<Message> messages, string? systemPrompt)
    {
        // Rough heuristic: ~4 characters per token
        long chars = 0;
        if (!string.IsNullOrEmpty(systemPrompt)) chars += systemPrompt!.Length;

        foreach (var m in messages)
        {
            foreach (var part in m.Parts)
            {
                switch (part)
                {
                    case TextPart t:
                        chars += t.Content?.Length ?? 0;
                        break;
                    case ToolCallPart tc:
                        try { chars += tc.Arguments.GetRawText().Length; } catch { }
                        break;
                    case ToolResultPart tr:
                        chars += tr.Content?.Length ?? 0;
                        break;
                }
            }
        }

        return (int)Math.Ceiling(chars / 4.0);
    }

    

    private static Message ConvertResponseToMessage(LLMResponse response, string sessionId, string providerName)
    {
        var parts = new List<MessagePart> { new TextPart(response.Content) };
        return Message.Create(sessionId, MessageRole.Assistant, parts, null, providerName);
    }

    private async Task<LLMResponse> SendWithCacheAsync(ILLMProvider provider, LLMRequest request, CancellationToken cancellationToken)
    {
        var preparation = await PrepareRequestAsync(provider, request, cancellationToken).ConfigureAwait(false);
        var response = await provider.SendAsync(preparation.RequestToSend, cancellationToken).ConfigureAwait(false);
        return response;
    }

    private async IAsyncEnumerable<LLMStreamChunk> StreamWithCacheAsync(
        ILLMProvider provider,
        LLMRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var preparation = await PrepareRequestAsync(provider, request, cancellationToken).ConfigureAwait(false);
        await foreach (var chunk in provider.StreamAsync(preparation.RequestToSend, cancellationToken))
        {
            yield return chunk;
        }
    }

    private async Task<(LLMRequest RequestToSend, PromptCacheContext? Context)> PrepareRequestAsync(
        ILLMProvider provider,
        LLMRequest request,
        CancellationToken cancellationToken)
    {
        if (_promptCache == null || !_cacheOptions.Enabled || string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            return (request with { UseEphemeralCache = false }, null);
        }

        var prepared = request with { UseEphemeralCache = true };
        return (prepared, null);
    }
}
