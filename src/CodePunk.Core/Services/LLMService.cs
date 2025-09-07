using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;

namespace CodePunk.Core.Services;

/// <summary>
/// Service for managing LLM providers and routing requests
/// </summary>
public interface ILLMService
{
    /// <summary>
    /// Get all available LLM providers
    /// </summary>
    IReadOnlyList<ILLMProvider> GetProviders();

    /// <summary>
    /// Get a specific provider by name
    /// </summary>
    ILLMProvider? GetProvider(string name);

    /// <summary>
    /// Get the default provider
    /// </summary>
    ILLMProvider GetDefaultProvider();

    /// <summary>
    /// Send a request using the default provider
    /// </summary>
    Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a request using a specific provider
    /// </summary>
    Task<LLMResponse> SendAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream a request using the default provider
    /// </summary>
    IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream a request using a specific provider
    /// </summary>
    IAsyncEnumerable<LLMStreamChunk> StreamAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a message using the default provider (convenience method)
    /// </summary>
    Task<Message> SendMessageAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream a message using the default provider (convenience method)
    /// </summary>
    IAsyncEnumerable<LLMStreamChunk> SendMessageStreamAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of LLM service
/// </summary>
public class LLMService : ILLMService
{
    private readonly ILLMProviderFactory _providerFactory;
    private readonly IPromptProvider _promptProvider;
    private readonly IToolService _toolService;

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
            try
            {
                providers.Add(_providerFactory.GetProvider(providerName));
            }
            catch
            {
            }
        }
        return providers;
    }

    public ILLMProvider? GetProvider(string name)
    {
        try
        {
            return _providerFactory.GetProvider(name);
        }
        catch
        {
            return null;
        }
    }

    public ILLMProvider GetDefaultProvider() => _providerFactory.GetProvider();

    public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default) =>
        GetDefaultProvider().SendAsync(request, cancellationToken);

    public Task<LLMResponse> SendAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default)
    {
        var provider = _providerFactory.GetProvider(providerName);
        return provider.SendAsync(request, cancellationToken);
    }

    public IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, CancellationToken cancellationToken = default) =>
        GetDefaultProvider().StreamAsync(request, cancellationToken);

    public IAsyncEnumerable<LLMStreamChunk> StreamAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default)
    {
        var provider = _providerFactory.GetProvider(providerName);
        return provider.StreamAsync(request, cancellationToken);
    }

    public async Task<Message> SendMessageAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default)
    {
        var provider = GetDefaultProvider();
        var request = ConvertMessagesToRequest(conversationHistory, provider.Name);
        var response = await provider.SendAsync(request, cancellationToken);
        return ConvertResponseToMessage(response, conversationHistory.Last().SessionId, provider.Name);
    }

    public IAsyncEnumerable<LLMStreamChunk> SendMessageStreamAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default)
    {
        var provider = GetDefaultProvider();
        var request = ConvertMessagesToRequest(conversationHistory, provider.Name);
        return provider.StreamAsync(request, cancellationToken);
    }

    private LLMRequest ConvertMessagesToRequest(IList<Message> messages, string providerName)
    {
        var systemPrompt = _promptProvider.GetSystemPrompt(providerName, PromptType.Coder);
        var provider = _providerFactory.GetProvider(providerName);
        var defaultModel = provider.Models.FirstOrDefault()?.Id ?? "gpt-4o";
        
        return new LLMRequest
        {
            ModelId = defaultModel,
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
        
        return Message.Create(
            sessionId,
            MessageRole.Assistant,
            parts,
            null, // Model should come from response metadata
            providerName
        );
    }
}
