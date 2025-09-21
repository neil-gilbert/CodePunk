using CodePunk.Core.Abstractions;
using CodePunk.Core.Providers;
using CodePunk.Core.Providers.Anthropic;
using CodePunk.Core.Services;
using CodePunk.Console.Stores;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CodePunk.Console.Providers;

/// <summary>
/// Dynamically (re)builds provider registrations from env, config, or persisted auth store.
/// </summary>
public class ProviderBootstrap
{
    private readonly IConfiguration _configuration;
    private readonly IAuthStore _authStore;
    private readonly ILogger<ProviderBootstrap> _logger;

    public ProviderBootstrap(IServiceCollection services, IConfiguration configuration, IAuthStore authStore, ILogger<ProviderBootstrap> logger)
    {
        _configuration = configuration;
        _authStore = authStore;
        _logger = logger;
    }

    public async Task ApplyAsync(CancellationToken ct = default)
    {
        var auth = await _authStore.LoadAsync(ct);

        string openAIApiKey = _configuration["AI:Providers:OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(openAIApiKey) && auth.TryGetValue("openai", out var storedOpenAi))
            openAIApiKey = storedOpenAi;

        string anthropicApiKey = _configuration["AI:Providers:Anthropic:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(anthropicApiKey) && auth.TryGetValue("anthropic", out var storedAnthropic))
            anthropicApiKey = storedAnthropic;

        static string Sanitize(string value) => (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
        openAIApiKey = Sanitize(openAIApiKey);
        anthropicApiKey = Sanitize(anthropicApiKey);

        // Remove existing registrations for providers (simplistic: rely on new service collection additions overshadow when building a new provider scope)
        // For simplicity we just append; factory will resolve latest due to GetService returning last.

    // OpenAI provider intentionally disabled until implementation is finalized.
    // if (!string.IsNullOrWhiteSpace(openAIApiKey)) { /* registration withheld */ }

        if (!string.IsNullOrWhiteSpace(anthropicApiKey))
        {
            _logger.LogInformation("Registering Anthropic provider via bootstrap (runtime registry).");
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
            var providerLoggerFactory = LoggerFactory.Create(b =>
            {
                var envLevel = Environment.GetEnvironmentVariable("CODEPUNK_PROVIDER_LOGLEVEL");
                if (!Enum.TryParse<LogLevel>(envLevel, true, out var level))
                {
                    level = LogLevel.Warning; // Default: suppress Info/Debug unless explicitly enabled
                }
                b.AddFilter("CodePunk.Core.Providers.Anthropic.AnthropicProvider", level)
                 .AddConsole();
            });
            var providerLogger = providerLoggerFactory.CreateLogger<AnthropicProvider>();
            var provider = new AnthropicProvider(http, config, providerLogger);
            CodePunk.Core.Services.RuntimeProviderRegistry.RegisterOrUpdate(provider);
        }
    }
}