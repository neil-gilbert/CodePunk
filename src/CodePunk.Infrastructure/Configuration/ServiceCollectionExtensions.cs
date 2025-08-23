using CodePunk.Core.Abstractions;
using CodePunk.Core.Services;
using CodePunk.Core.Tools;
using CodePunk.Core.Providers;
using CodePunk.Core.Chat;
using CodePunk.Data;
using CodePunk.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        // Add LLM providers
        services.AddHttpClient<OpenAIProvider>();
        services.AddScoped<ILLMProvider>(provider =>
        {
            var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(OpenAIProvider));
            
            var config = new LLMProviderConfig
            {
                Name = "OpenAI",
                ApiKey = configuration["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "",
                BaseUrl = configuration["OpenAI:BaseUrl"]
            };

            return new OpenAIProvider(httpClient, config);
        });

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
