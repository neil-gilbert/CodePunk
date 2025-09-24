using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;

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
    private string? _overrideProvider;
    private string? _overrideModel;

    public LLMService(ILLMProviderFactory providerFactory, IPromptProvider promptProvider, IToolService toolService)
    {
        _providerFactory = providerFactory;
        _promptProvider = promptProvider;
        _toolService = toolService;
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
        ResolveProvider().SendAsync(request, cancellationToken);

    public Task<LLMResponse> SendAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default)
    {
        var provider = _providerFactory.GetProvider(providerName);
        return provider.SendAsync(request, cancellationToken);
    }

    public IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, CancellationToken cancellationToken = default) =>
        ResolveProvider().StreamAsync(request, cancellationToken);

    public IAsyncEnumerable<LLMStreamChunk> StreamAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default)
    {
        var provider = _providerFactory.GetProvider(providerName);
        return provider.StreamAsync(request, cancellationToken);
    }

    public async Task<Message> SendMessageAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider();
        var request = ConvertMessagesToRequest(conversationHistory, provider.Name);
        var response = await provider.SendAsync(request, cancellationToken);
        return ConvertResponseToMessage(response, conversationHistory.Last().SessionId, provider.Name);
    }

    public IAsyncEnumerable<LLMStreamChunk> SendMessageStreamAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider();
        var request = ConvertMessagesToRequest(conversationHistory, provider.Name);
        return provider.StreamAsync(request, cancellationToken);
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
}
