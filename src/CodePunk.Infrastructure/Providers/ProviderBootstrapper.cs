using CodePunk.Core.Abstractions;
using CodePunk.Core.Caching;
using CodePunk.Core.Providers;
using CodePunk.Core.Providers.Anthropic;
using CodePunk.Core.Services;
using CodePunk.Infrastructure.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CodePunk.Infrastructure.Providers;

public class ProviderBootstrapper
{
    private readonly IConfiguration _configuration;
    private readonly IAuthStore _authStore;
    private readonly IServiceProvider _services;
    private readonly ILogger<ProviderBootstrapper> _logger;

    public ProviderBootstrapper(
        IConfiguration configuration,
        IAuthStore authStore,
        IServiceProvider services,
        ILogger<ProviderBootstrapper> logger)
    {
        _configuration = configuration;
        _authStore = authStore;
        _services = services;
        _logger = logger;
    }

    public async Task ApplyAsync(CancellationToken ct = default)
    {
        var creds = await _authStore.LoadAsync(ct).ConfigureAwait(false);

        string openAIApiKey = _configuration["AI:Providers:OpenAI:ApiKey"]
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? (creds.TryGetValue("openai", out var openaiKey) ? openaiKey : string.Empty);

        string anthropicApiKey = _configuration["AI:Providers:Anthropic:ApiKey"]
            ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? (creds.TryGetValue("anthropic", out var anthropicKey) ? anthropicKey : string.Empty);

        static string Sanitize(string value) => (value ?? string.Empty)
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty)
            .Trim();

        openAIApiKey = Sanitize(openAIApiKey);
        anthropicApiKey = Sanitize(anthropicApiKey);

        if (!string.IsNullOrWhiteSpace(anthropicApiKey))
        {
            try
            {
                var baseUrl = _configuration["AI:Providers:Anthropic:BaseUrl"] ?? "https://api.anthropic.com/v1";
                var version = _configuration["AI:Providers:Anthropic:Version"] ?? "2023-06-01";
                var defaultModel = _configuration["AI:Providers:Anthropic:DefaultModel"] ?? AnthropicModels.ClaudeOpus41;

                var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
                var config = new AnthropicConfiguration
                {
                    ApiKey = anthropicApiKey,
                    BaseUrl = baseUrl,
                    DefaultModel = defaultModel,
                    MaxTokens = 4096,
                    Temperature = 0.7,
                    Version = version
                };

                var logger = _services.GetRequiredService<ILogger<AnthropicProvider>>();
                var cacheOptions = _services.GetService<Microsoft.Extensions.Options.IOptions<PromptCacheOptions>>()?.Value
                                   ?? new PromptCacheOptions();

                var provider = new AnthropicProvider(http, config, cacheOptions, logger);
                RuntimeProviderRegistry.RegisterOrUpdate(provider);
                _logger.LogInformation("Registered Anthropic provider from persisted credentials");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to register Anthropic provider");
            }
        }

        if (!string.IsNullOrWhiteSpace(openAIApiKey))
        {
            try
            {
                var baseUrl = _configuration["AI:Providers:OpenAI:BaseUrl"] ?? "https://api.openai.com/v1";
                var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
                var config = new LLMProviderConfig
                {
                    Name = "OpenAI",
                    ApiKey = openAIApiKey,
                    BaseUrl = baseUrl
                };

                var provider = new OpenAIProvider(http, config);
                RuntimeProviderRegistry.RegisterOrUpdate(provider);
                _logger.LogInformation("Registered OpenAI provider from persisted credentials");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to register OpenAI provider");
            }
        }
    }
}

