using CodePunk.Core.Abstractions;
using CodePunk.Core.Configuration;
using CodePunk.Core.Caching;
using CodePunk.Core.Services;
using CodePunk.Core.Tools;
using CodePunk.Core.Providers;
using CodePunk.Core.Providers.Anthropic;
using CodePunk.Core.Chat;
using CodePunk.Core.SyntaxHighlighting;
using CodePunk.Core.SyntaxHighlighting.Abstractions;
using CodePunk.Core.SyntaxHighlighting.Languages;
using CodePunk.Data;
using CodePunk.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CodePunk.Roslyn.Abstractions;
using CodePunk.Roslyn.Services;
using CodePunk.Roslyn.Tools;

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
        services.AddScoped<ILLMTokenService, LLMTokenService>();
        // Tracing disabled by default; enable via temporary diagnostics only
        services.AddScoped<IToolService, ToolService>();

        services.AddScoped<InteractiveChatSession>();

        services.AddScoped<IDiffService, DiffService>();
        services.AddScoped<IApprovalService, ConsoleApprovalService>();
        services.AddScoped<IFileEditService, FileEditService>();

        services.Configure<PromptCacheOptions>(configuration.GetSection("PromptCache"));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPromptCacheKeyBuilder, DefaultPromptCacheKeyBuilder>();
        services.AddSingleton<IPromptCacheStore, InMemoryPromptCacheStore>();
        services.AddSingleton<IPromptCache, PromptCache>();

        // Syntax highlighting
        services.AddSingleton<ISyntaxHighlighter, SyntaxHighlighter>();
        services.AddSingleton<ILanguageDefinition, CSharpLanguageDefinition>();
        services.AddSingleton<ILanguageDefinition, JavaScriptLanguageDefinition>();
        services.AddSingleton<ILanguageDefinition, SqlLanguageDefinition>();
        services.AddSingleton<ILanguageDefinition, PythonLanguageDefinition>();
        services.AddSingleton<ILanguageDefinition, TypeScriptLanguageDefinition>();
        services.AddSingleton<ILanguageDefinition, GoLanguageDefinition>();
        services.AddSingleton<ILanguageDefinition, JavaLanguageDefinition>();

        services.Configure<ShellCommandOptions>(configuration.GetSection(ShellCommandOptions.SectionName));

        services.AddScoped<ITool, ReadFileTool>();
        services.AddScoped<ITool, WriteFileTool>();
        services.AddScoped<ITool, ReplaceInFileTool>();
        services.AddScoped<ITool, ShellTool>();
        services.AddScoped<ITool, ListDirectoryTool>();
        services.AddScoped<ITool, GlobTool>();
        services.AddScoped<ITool, SearchFilesTool>();
        services.AddScoped<ITool, ReadManyFilesTool>();
        services.AddScoped<ITool, CodePunk.Core.Tools.Modes.PlanModeTool>();
        services.AddScoped<ITool, CodePunk.Core.Tools.Modes.BugModeTool>();
        services.AddScoped<ITool, CodePunk.Core.Tools.Planning.PlanGenerateTool>();

        // Roslyn services
        services.AddSingleton<IRoslynWorkspaceService, RoslynWorkspaceService>();
        services.AddScoped<IRoslynAnalyzerService, RoslynAnalyzerService>();
        services.AddScoped<IRoslynRefactorService, RoslynRefactorService>();

        // Gate Roslyn tool exposure to LLM by solution/project presence
        var cwd = Directory.GetCurrentDirectory();
        var hasSln = Directory.EnumerateFiles(cwd, "*.sln", SearchOption.TopDirectoryOnly).Any();
        var hasProj = Directory.EnumerateFiles(cwd, "*.csproj", SearchOption.TopDirectoryOnly).Any();
        if (hasSln || hasProj)
        {
            services.AddScoped<ITool, RoslynAnalyzeTool>();
            services.AddScoped<ITool, RoslynExplainSymbolTool>();
            services.AddScoped<ITool, RoslynRefactorTool>();
            services.AddScoped<ITool, RoslynCallGraphTool>();
            services.AddScoped<ITool, RoslynValidateBuildTool>();
        }

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
            var costSection = configuration.GetSection("AI:Providers:Anthropic:Costs");
            if (costSection.Exists())
            {
                foreach (var child in costSection.GetChildren())
                {
                    var model = child.Key;
                    var input = child.GetValue<decimal>("Input");
                    var output = child.GetValue<decimal>("Output");
                    if (input > 0 || output > 0)
                    {
                        anthropicConfig.Costs[model] = new AnthropicModelCost { Input = input, Output = output };
                    }
                }
            }

            services.AddSingleton(anthropicConfig);
            services.AddHttpClient<AnthropicProvider>()
                .AddStandardResilienceHandler();
            services.AddTransient<AnthropicProvider>(provider =>
            {
                var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
                var httpClient = httpClientFactory.CreateClient(nameof(AnthropicProvider));
                var logger = provider.GetRequiredService<ILogger<AnthropicProvider>>();
                var cacheOptions = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PromptCacheOptions>>().Value;
                return new AnthropicProvider(httpClient, anthropicConfig, cacheOptions, logger);
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
