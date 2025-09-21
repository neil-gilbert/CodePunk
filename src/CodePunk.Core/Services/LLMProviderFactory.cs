using CodePunk.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace CodePunk.Core.Services;

public interface ILLMProviderFactory
{
    ILLMProvider GetProvider(string? providerName = null);
    IEnumerable<string> GetAvailableProviders();
}

public class LLMProviderFactory : ILLMProviderFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;

    public LLMProviderFactory(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    public ILLMProvider GetProvider(string? providerName = null)
    {
        providerName ??= _configuration["AI:DefaultProvider"] ?? "openai";
        var normalized = providerName.ToLowerInvariant();

        // Try dynamic registry first
        if (RuntimeProviderRegistry.TryGet(normalized, out var dynamicProvider))
        {
            return dynamicProvider;
        }

        // OpenAI temporarily disabled; if requested, fall back to any available provider or dynamic ones
        if (normalized == "openai")
        {
            var available = GetAvailableProviders().ToList();
            var anthropicProvider = _serviceProvider.GetService<CodePunk.Core.Providers.Anthropic.AnthropicProvider>();
            if (anthropicProvider != null)
            {
                return anthropicProvider;
            }
            // dynamic registry second pass
            var dynamic = RuntimeProviderRegistry.GetNames().ToList();
            if (dynamic.Count > 0 && RuntimeProviderRegistry.TryGet(dynamic[0], out var dynProv))
            {
                return dynProv;
            }
            if (available.Count > 0)
            {
                // Attempt to resolve first available explicitly
                return GetProvider(available[0]);
            }
            throw new InvalidOperationException("No providers are configured. Run /setup or set an API key.");
        }

        return normalized switch
        {
            "anthropic" => _serviceProvider.GetService<CodePunk.Core.Providers.Anthropic.AnthropicProvider>()
                ?? throw new InvalidOperationException("Anthropic provider is not configured. Please set the ANTHROPIC_API_KEY environment variable or configure it in appsettings.json."),
            _ => throw new NotSupportedException($"Provider '{providerName}' is not supported. Available providers: {string.Join(", ", GetAvailableProviders())}")
        };
    }

    public IEnumerable<string> GetAvailableProviders()
    {
        var providers = new List<string>();

        providers.AddRange(RuntimeProviderRegistry.GetNames());

        if (_serviceProvider.GetService<CodePunk.Core.Providers.OpenAIProvider>() != null)
        {
            providers.Add("openai");
        }

        if (_serviceProvider.GetService<CodePunk.Core.Providers.Anthropic.AnthropicProvider>() != null)
        {
            providers.Add("anthropic");
        }

        return providers;
    }
}
