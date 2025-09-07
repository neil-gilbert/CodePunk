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

        return providerName.ToLowerInvariant() switch
        {
            "openai" => _serviceProvider.GetService<CodePunk.Core.Providers.OpenAIProvider>()
                ?? throw new InvalidOperationException("OpenAI provider is not configured. Please set the OPENAI_API_KEY environment variable or configure it in appsettings.json."),
            
            "anthropic" => _serviceProvider.GetService<CodePunk.Core.Providers.Anthropic.AnthropicProvider>()
                ?? throw new InvalidOperationException("Anthropic provider is not configured. Please set the ANTHROPIC_API_KEY environment variable or configure it in appsettings.json."),
            
            _ => throw new NotSupportedException($"Provider '{providerName}' is not supported. Available providers: {string.Join(", ", GetAvailableProviders())}")
        };
    }

    public IEnumerable<string> GetAvailableProviders()
    {
        var providers = new List<string>();

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
