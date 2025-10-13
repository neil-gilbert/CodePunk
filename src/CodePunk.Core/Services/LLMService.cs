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
        var provider = _providerFactory.GetProvider(providerName);
        var modelId = !string.IsNullOrWhiteSpace(_overrideModel) && provider.Models.Any(m => m.Id == _overrideModel)
            ? _overrideModel!
            : provider.Models.FirstOrDefault()?.Id ?? "gpt-4o";

        var msgs = messages.ToList();
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
            Temperature = 0.7,
            SystemPrompt = systemPrompt
        };
        return request;
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
        _trace?.TraceResponse(provider.Name, request.ModelId, request.Messages.LastOrDefault()?.SessionId ?? "", BuildRespSummary(response), response.Content);

        if (preparation.Context != null && _promptCache != null)
        {
            if (response.PromptCache != null)
            {
                await _promptCache.StoreAsync(preparation.Context, true, response.PromptCache, cancellationToken).ConfigureAwait(false);
            }
            else if (preparation.RequestToSend.UseEphemeralCache)
            {
                await _promptCache.StoreAsync(preparation.Context, false, null, cancellationToken).ConfigureAwait(false);
            }
        }

        return response;
    }

    private async IAsyncEnumerable<LLMStreamChunk> StreamWithCacheAsync(
        ILLMProvider provider,
        LLMRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var preparation = await PrepareRequestAsync(provider, request, cancellationToken).ConfigureAwait(false);
        var cacheStored = false;

        await foreach (var chunk in provider.StreamAsync(preparation.RequestToSend, cancellationToken))
        {
            if (!cacheStored && preparation.Context != null && _promptCache != null && chunk.PromptCache != null)
            {
                await _promptCache.StoreAsync(preparation.Context, true, chunk.PromptCache, cancellationToken).ConfigureAwait(false);
                cacheStored = true;
            }
            yield return chunk;
        }

        if (!cacheStored && preparation.Context != null && _promptCache != null && preparation.RequestToSend.UseEphemeralCache)
        {
            await _promptCache.StoreAsync(preparation.Context, false, null, cancellationToken).ConfigureAwait(false);
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

        var context = new PromptCacheContext(provider.Name, request);
        var existing = await _promptCache.TryGetAsync(context, cancellationToken).ConfigureAwait(false);

        if (existing != null)
        {
            if (existing.ProviderSupportsCache && existing.CacheInfo != null)
            {
                var prepared = request with
                {
                    SystemPromptCacheId = existing.CacheInfo.CacheId,
                    UseEphemeralCache = false,
                    SystemPrompt = null
                };
                return (prepared, context);
            }

            return (request with { UseEphemeralCache = false }, context);
        }

        var attempt = request with { UseEphemeralCache = true };
        return (attempt, context);
    }
}
