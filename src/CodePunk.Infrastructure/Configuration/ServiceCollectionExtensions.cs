using CodePunk.Core.Abstractions;
using CodePunk.Core.Configuration;
using CodePunk.Core.Services;
using CodePunk.Core.Tools;
using CodePunk.Core.Providers;
using CodePunk.Core.Providers.Anthropic;
using CodePunk.Core.Chat;
using CodePunk.Core.Git;
using CodePunk.Core.GitSession;
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
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? "Data Source=codepunk.db";

        services.AddDbContext<CodePunkDbContext>(options =>
        {
            options.UseSqlite(connectionString, sqliteOptions =>
            {
                sqliteOptions.CommandTimeout(30);
            });
            
            options.EnableServiceProviderCaching();
            options.EnableDetailedErrors(false); // Disable in production
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

        services.AddScoped<ISessionSummarizer, HeuristicSessionSummarizer>();

        services.AddSingleton<IPromptProvider, PromptProvider>();

        services.AddScoped<ILLMService, LLMService>();

        // Always register Git Session services so they're available for cleanup
        // They will self-disable based on configuration
        if (configuration != null)
        {
            services.Configure<GitSessionOptions>(configuration.GetSection("GitSession"));
        }
        else
        {
            services.Configure<GitSessionOptions>(options => options.Enabled = false);
        }

        // Core git services - now scoped for worktree support
        services.AddScoped<IWorkingDirectoryProvider, DefaultWorkingDirectoryProvider>();
        services.AddSingleton<IGitCommandExecutor, GitCommandExecutor>();
        services.AddSingleton<IGitSessionStateStore, GitSessionStateStore>();
        services.AddScoped<IGitSessionService, GitSessionService>();

        var gitSessionOptions = configuration?.GetSection("GitSession").Get<GitSessionOptions>();
        var gitSessionEnabled = gitSessionOptions?.Enabled ?? false;

        if (configuration != null && gitSessionEnabled)
        {
            services.AddHostedService<GitSessionCleanupService>();

            services.AddScoped<ToolService>();
            services.AddScoped<IToolService>(sp =>
            {
                var toolService = sp.GetRequiredService<ToolService>();
                var gitSessionService = sp.GetRequiredService<IGitSessionService>();
                var logger = sp.GetRequiredService<ILogger<GitSessionToolInterceptor>>();
                return new GitSessionToolInterceptor(toolService, gitSessionService, logger);
            });
        }
        else
        {
            services.AddScoped<IToolService, ToolService>();
        }

        services.AddScoped<InteractiveChatSession>();

        services.AddScoped<IDiffService, DiffService>();
        services.AddScoped<IApprovalService, ConsoleApprovalService>();
        services.AddScoped<IFileEditService, FileEditService>();


        services.Configure<ShellCommandOptions>(configuration.GetSection(ShellCommandOptions.SectionName));

        services.AddScoped<ITool, ReadFileTool>();
        services.AddScoped<ITool, WriteFileTool>();
        services.AddScoped<ITool, ReplaceInFileTool>();
        services.AddScoped<ITool, ShellTool>();
        services.AddScoped<ITool, ListDirectoryTool>();
        services.AddScoped<ITool, GlobTool>();
        services.AddScoped<ITool, SearchFilesTool>();
        services.AddScoped<ITool, ReadManyFilesTool>();

        services.AddLLMProviders(configuration);

        return services;
    }

    public static IServiceCollection AddLLMProviders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<ILLMProviderFactory, LLMProviderFactory>();

        var openAIApiKey = configuration["AI:Providers:OpenAI:ApiKey"] ?? 
                          configuration["OpenAI:ApiKey"] ?? 
                          Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";

            if (!string.IsNullOrEmpty(openAIApiKey))
            {
                services.AddHttpClient<OpenAIProvider>()
                    .AddStandardResilienceHandler(); 
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
