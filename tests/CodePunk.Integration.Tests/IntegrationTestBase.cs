using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Services;
using CodePunk.Data;
using CodePunk.Data.Repositories;
using CodePunk.Core.Providers.Anthropic;
using Microsoft.Extensions.Configuration;

namespace CodePunk.Integration.Tests;

/// <summary>
/// Base class for integration tests that use the Ports and Adapters pattern.
/// Tests interact with services through their interfaces, not directly with the database.
/// </summary>
public abstract class IntegrationTestBase : IDisposable
{
    protected readonly IServiceProvider ServiceProvider;
    protected readonly ISessionService SessionService;
    protected readonly IMessageService MessageService;
    protected readonly IFileHistoryService FileHistoryService;
    
    private readonly SqliteConnection _connection;

    protected IntegrationTestBase()
    {
        // Create shared in-memory SQLite connection
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        // Set up configuration for Anthropic
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:DefaultProvider"] = "anthropic",
                ["Anthropic:ApiKey"] = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "test-key",
                ["Anthropic:Version"] = "2023-06-01"
            })
            .Build();

        // Create service collection with production services
        var services = new ServiceCollection();
        
        services.AddSingleton<IConfiguration>(configuration);
        
        // Add in-memory database with shared connection
        services.AddDbContext<CodePunkDbContext>(options =>
            options.UseSqlite(_connection));
            
        // Register repositories (data adapters)
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IFileHistoryRepository, FileHistoryRepository>();
        
        // Register services (ports)
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<IFileHistoryService, FileHistoryService>();
        services.AddScoped<IToolService, ToolService>();
        services.AddScoped<IPromptProvider, PromptProvider>();
        
        // Register LLM Provider Factory and Providers
        services.AddSingleton<ILLMProviderFactory, LLMProviderFactory>();
        services.AddHttpClient<AnthropicProvider>(); // Registers typed client
        services.AddScoped<ILLMProvider, AnthropicProvider>(sp => sp.GetRequiredService<AnthropicProvider>());

        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var anthropicConfig = new AnthropicConfiguration();
            config.GetSection("Anthropic").Bind(anthropicConfig);
            return anthropicConfig;
        });
        services.AddScoped<ILLMService, LLMService>();

        // Add logging
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        
        ServiceProvider = services.BuildServiceProvider();
        
        // Initialize database schema
        using var scope = ServiceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CodePunkDbContext>();
        context.Database.EnsureCreated();
        
        // Get services through DI (testing through the ports)
        SessionService = ServiceProvider.GetRequiredService<ISessionService>();
        MessageService = ServiceProvider.GetRequiredService<IMessageService>();
        FileHistoryService = ServiceProvider.GetRequiredService<IFileHistoryService>();
    }

    protected IServiceScope CreateScope() => ServiceProvider.CreateScope();

    public void Dispose()
    {
        if (ServiceProvider is IDisposable disposable)
            disposable.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
