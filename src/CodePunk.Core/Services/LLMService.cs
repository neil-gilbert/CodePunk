using System.Runtime.CompilerServices;
using System.Text;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Caching;
using CodePunk.Core.Models;
using Microsoft.Extensions.Options;

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
    private string? _overrideProvider;
    private string? _overrideModel;

    public LLMService(
        ILLMProviderFactory providerFactory,
        IPromptProvider promptProvider,
        IToolService toolService,
        IPromptCache? promptCache = null,
        IOptions<PromptCacheOptions>? cacheOptions = null)
    {
        _providerFactory = providerFactory;
        _promptProvider = promptProvider;
        _toolService = toolService;
        _promptCache = promptCache;
        _cacheOptions = cacheOptions?.Value ?? new PromptCacheOptions();
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

        return new LLMRequest
        {
            ModelId = modelId,
            Messages = messages.ToList().AsReadOnly(),
            Tools = _toolService.GetLLMTools(),
            MaxTokens = 4096,
            Temperature = 0.7,
            SystemPrompt = systemPrompt
        };
    }

    private static Message ConvertResponseToMessage(LLMResponse response, string sessionId, string providerName)
    {
        var parts = new List<MessagePart> { new TextPart(response.Content) };
        return Message.Create(sessionId, MessageRole.Assistant, parts, null, providerName);
    }

    private async Task<LLMResponse> SendWithCacheAsync(ILLMProvider provider, LLMRequest request, CancellationToken cancellationToken)
    {
        if (_promptCache == null || !_cacheOptions.Enabled)
        {
            return await provider.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var context = new PromptCacheContext(provider.Name, request);
        var cached = await _promptCache.TryGetAsync(context, cancellationToken).ConfigureAwait(false);
        if (cached != null)
        {
            return cached.Response;
        }

        var response = await provider.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await _promptCache.StoreAsync(context, response, cancellationToken).ConfigureAwait(false);
        return response;
    }

    private async IAsyncEnumerable<LLMStreamChunk> StreamWithCacheAsync(
        ILLMProvider provider,
        LLMRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_promptCache == null || !_cacheOptions.Enabled)
        {
            await foreach (var chunk in provider.StreamAsync(request, cancellationToken))
            {
                yield return chunk;
            }
            yield break;
        }

        var context = new PromptCacheContext(provider.Name, request);
        var cached = await _promptCache.TryGetAsync(context, cancellationToken).ConfigureAwait(false);
        if (cached != null)
        {
            foreach (var chunk in ReplayCachedResponse(cached.Response))
            {
                yield return chunk;
            }
            yield break;
        }

        var contentBuilder = new StringBuilder();
        var toolCalls = new List<ToolCall>();
        LLMUsage? usage = null;
        var finishReason = LLMFinishReason.Stop;

        await foreach (var chunk in provider.StreamAsync(request, cancellationToken))
        {
            if (!string.IsNullOrEmpty(chunk.Content))
            {
                contentBuilder.Append(chunk.Content);
            }

            if (chunk.ToolCall != null)
            {
                toolCalls.Add(chunk.ToolCall);
            }

            if (chunk.Usage != null)
            {
                usage = chunk.Usage;
            }

            if (chunk.FinishReason.HasValue)
            {
                finishReason = chunk.FinishReason.Value;
            }

            yield return chunk;
        }

        var response = new LLMResponse
        {
            Content = contentBuilder.ToString(),
            ToolCalls = toolCalls.Count > 0 ? toolCalls.ToArray() : null,
            Usage = usage,
            FinishReason = finishReason
        };

        await _promptCache.StoreAsync(context, response, cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<LLMStreamChunk> ReplayCachedResponse(LLMResponse response)
    {
        if (response.ToolCalls != null)
        {
            foreach (var toolCall in response.ToolCalls)
            {
                yield return new LLMStreamChunk
                {
                    ToolCall = toolCall,
                    IsComplete = false
                };
            }
        }

        yield return new LLMStreamChunk
        {
            Content = response.Content ?? string.Empty,
            Usage = response.Usage,
            FinishReason = response.FinishReason,
            IsComplete = true
        };
    }
}
