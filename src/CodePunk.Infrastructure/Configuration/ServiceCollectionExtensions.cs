using CodePunk.Core.Abstractions;
using CodePunk.Core.Services;
using CodePunk.Core.Tools;
using CodePunk.Core.Providers;
using CodePunk.Core.Providers.Anthropic;
using CodePunk.Core.Chat;
using CodePunk.Data;
using CodePunk.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CodePunk.Infrastructure.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCodePunkServices(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        // Add database with performance optimizations
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? "Data Source=codepunk.db";

        services.AddDbContext<CodePunkDbContext>(options =>
        {
            options.UseSqlite(connectionString, sqliteOptions =>
            {
                sqliteOptions.CommandTimeout(30);
            });
            
            // Performance optimizations
            options.EnableServiceProviderCaching();
            options.EnableDetailedErrors(false); // Disable in production
            
            // Disable change tracking for read queries by default
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });

        // Add repositories
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IFileHistoryRepository, FileHistoryRepository>();

        // Add domain services
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<IFileHistoryService, FileHistoryService>();

    // Session summarizer (heuristic/default)
    services.AddScoped<ISessionSummarizer, HeuristicSessionSummarizer>();

        // Add prompt services
        services.AddSingleton<IPromptProvider, PromptProvider>();

        // Add LLM services
        services.AddScoped<ILLMService, LLMService>();
        services.AddScoped<IToolService, ToolService>();

        // Add chat services
        services.AddScoped<InteractiveChatSession>();


        // Add tools
    services.AddScoped<ITool, ReadFileTool>();
    services.AddScoped<ITool, WriteFileTool>();
    services.AddScoped<ITool, ShellTool>();
    services.AddScoped<ITool, ApplyDiffTool>();

        // Add LLM providers
        services.AddLLMProviders(configuration);

        return services;
    }

    public static IServiceCollection AddLLMProviders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register provider factory
        services.AddSingleton<ILLMProviderFactory, LLMProviderFactory>();

        // Add OpenAI provider
        var openAIApiKey = configuration["AI:Providers:OpenAI:ApiKey"] ?? 
                          configuration["OpenAI:ApiKey"] ?? // Backward compatibility
                          Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";

            if (!string.IsNullOrEmpty(openAIApiKey))
            {
                services.AddHttpClient<OpenAIProvider>()
                    .AddStandardResilienceHandler(); // Retry, timeout, circuit breaker
            services.AddTransient<OpenAIProvider>(provider =>
            {
                var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
                var httpClient = httpClientFactory.CreateClient(nameof(OpenAIProvider));
                
                var config = new LLMProviderConfig
                {
                    Name = "OpenAI",
                    ApiKey = openAIApiKey,
                    BaseUrl = configuration["AI:Providers:OpenAI:BaseUrl"] ?? 
                             configuration["OpenAI:BaseUrl"] ?? 
                             "https://api.openai.com/v1"
                };

                return new OpenAIProvider(httpClient, config);
            });
        }

        // Add Anthropic provider
        var anthropicApiKey = configuration["AI:Providers:Anthropic:ApiKey"] ?? 
                             Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";

            if (!string.IsNullOrEmpty(anthropicApiKey))
            {
            var anthropicConfig = new AnthropicConfiguration
            {
                ApiKey = anthropicApiKey,
                BaseUrl = configuration["AI:Providers:Anthropic:BaseUrl"] ?? "https://api.anthropic.com/v1",
                DefaultModel = configuration["AI:Providers:Anthropic:DefaultModel"] ?? AnthropicModels.ClaudeOpus41,
                MaxTokens = configuration.GetValue("AI:Providers:Anthropic:MaxTokens", 4096),
                Temperature = configuration.GetValue("AI:Providers:Anthropic:Temperature", 0.7),
                Version = configuration["AI:Providers:Anthropic:Version"] ?? "2023-06-01"
            };

            services.AddSingleton(anthropicConfig);
                services.AddHttpClient<AnthropicProvider>()
                    .AddStandardResilienceHandler();
            services.AddTransient<AnthropicProvider>(provider =>
            {
                var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
                var httpClient = httpClientFactory.CreateClient(nameof(AnthropicProvider));
                var logger = provider.GetRequiredService<ILogger<AnthropicProvider>>();
                
                return new AnthropicProvider(httpClient, anthropicConfig, logger);
            });
        }

        return services;
    }

    public static async Task<IServiceProvider> EnsureDatabaseCreatedAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CodePunkDbContext>();
        await context.Database.EnsureCreatedAsync();
        return serviceProvider;
    }
}
