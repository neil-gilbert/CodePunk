using CodePunk.Core.Abstractions;

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
}

/// <summary>
/// Implementation of LLM service
/// </summary>
public class LLMService : ILLMService
{
    private readonly IReadOnlyList<ILLMProvider> _providers;
    private readonly string _defaultProviderName;

    public LLMService(IEnumerable<ILLMProvider> providers, string defaultProviderName = "OpenAI")
    {
        _providers = providers.ToList();
        _defaultProviderName = defaultProviderName;
        
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
}
