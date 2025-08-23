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
    private readonly IReadOnlyList<ILLMProvider> _providers;
    private readonly string _defaultProviderName;
    private readonly IPromptProvider _promptProvider;

    public LLMService(IEnumerable<ILLMProvider> providers, IPromptProvider promptProvider, string defaultProviderName = "OpenAI")
    {
        _providers = providers.ToList();
        _defaultProviderName = defaultProviderName;
        _promptProvider = promptProvider;
        
        if (!_providers.Any())
        {
            throw new InvalidOperationException("No LLM providers registered");
        }
    }

    public IReadOnlyList<ILLMProvider> GetProviders() => _providers;

    public ILLMProvider? GetProvider(string name) =>
        _providers.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public ILLMProvider GetDefaultProvider()
    {
        var provider = GetProvider(_defaultProviderName) ?? _providers.First();
        return provider;
    }

    public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default) =>
        GetDefaultProvider().SendAsync(request, cancellationToken);

    public Task<LLMResponse> SendAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default)
    {
        var provider = GetProvider(providerName) 
            ?? throw new ArgumentException($"Provider '{providerName}' not found", nameof(providerName));
        return provider.SendAsync(request, cancellationToken);
    }

    public IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, CancellationToken cancellationToken = default) =>
        GetDefaultProvider().StreamAsync(request, cancellationToken);

    public IAsyncEnumerable<LLMStreamChunk> StreamAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default)
    {
        var provider = GetProvider(providerName) 
            ?? throw new ArgumentException($"Provider '{providerName}' not found", nameof(providerName));
        return provider.StreamAsync(request, cancellationToken);
    }

    public async Task<Message> SendMessageAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default)
    {
        var provider = GetDefaultProvider();
        var request = ConvertMessagesToRequest(conversationHistory, provider.Name);
        var response = await provider.SendAsync(request, cancellationToken);
        return ConvertResponseToMessage(response, conversationHistory.Last().SessionId);
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
        
        return new LLMRequest
        {
            ModelId = "gpt-4o", // Default model - should be configurable
            Messages = messages.ToList().AsReadOnly(),
            MaxTokens = 4096,
            Temperature = 0.7,
            SystemPrompt = systemPrompt
        };
    }

    private static Message ConvertResponseToMessage(LLMResponse response, string sessionId)
    {
        var parts = new List<MessagePart> { new TextPart(response.Content) };
        
        return Message.Create(
            sessionId,
            MessageRole.Assistant,
            parts,
            "gpt-4o", // Should come from response metadata
            "OpenAI"  // Should come from provider
        );
    }
}
