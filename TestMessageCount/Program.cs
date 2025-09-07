using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using CodePunk.Console.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using CodePunk.Data;
using CodePunk.Data.Repositories;
using CodePunk.Core.Services;
using Spectre.Console;
using Microsoft.Extensions.Logging;

var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["AI:DefaultProvider"] = "anthropic",
        ["Anthropic:ApiKey"] = "test-key",
        ["Anthropic:Version"] = "2023-06-01"
    })
    .Build();

using var connection = new SqliteConnection("DataSource=:memory:");
connection.Open();

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(configuration);
services.AddDbContext<CodePunkDbContext>(options => options.UseSqlite(connection));
services.AddScoped<ISessionRepository, SessionRepository>();
services.AddScoped<IMessageRepository, MessageRepository>();
services.AddScoped<ISessionService, SessionService>();
services.AddScoped<IMessageService, MessageService>();
services.AddLogging(); // Add logging services

var serviceProvider = services.BuildServiceProvider();

// Create database
using (var scope = serviceProvider.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<CodePunkDbContext>();
    context.Database.EnsureCreated();
}

// Test the scenario
using (var scope = serviceProvider.CreateScope())
{
    var sessionService = scope.ServiceProvider.GetRequiredService<ISessionService>();
    var messageService = scope.ServiceProvider.GetRequiredService<IMessageService>();
    
    // Create a session
    Console.WriteLine("Creating session...");
    var session = await sessionService.CreateAsync("Test Session");
    Console.WriteLine($"Created session: {session.Id}");
    
    // Add some messages
    Console.WriteLine("Adding messages...");
    await messageService.CreateAsync(new Message 
    { 
        Id = Guid.NewGuid().ToString(),
        SessionId = session.Id,
        Role = MessageRole.User,
        Parts = [new TextPart("Hello")],
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    });
    
    await messageService.CreateAsync(new Message 
    { 
        Id = Guid.NewGuid().ToString(),
        SessionId = session.Id,
        Role = MessageRole.Assistant,
        Parts = [new TextPart("Hi there!")],
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    });
    
    await messageService.CreateAsync(new Message 
    { 
        Id = Guid.NewGuid().ToString(),
        SessionId = session.Id,
        Role = MessageRole.User,
        Parts = [new TextPart("How are you?")],
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    });
    
    Console.WriteLine("Added 3 messages");
    
    // Test the sessions command
    Console.WriteLine("\nTesting SessionsCommand...");
    var sessionsCommand = new SessionsCommand(sessionService, messageService);
    
    var result = await sessionsCommand.ExecuteAsync([], CancellationToken.None);
    
    Console.WriteLine($"\nCommand result: Success={result.Success}");
    
    // Also verify directly
    var retrievedSession = await sessionService.GetByIdAsync(session.Id);
    Console.WriteLine($"Direct session check - MessageCount: {retrievedSession?.MessageCount}");
    
    var recentSessions = await sessionService.GetRecentAsync(10);
    var foundSession = recentSessions.FirstOrDefault(s => s.Id == session.Id);
    Console.WriteLine($"Recent sessions check - MessageCount: {foundSession?.MessageCount}");
    
    if (foundSession?.MessageCount == 3)
    {
        Console.WriteLine("✅ SUCCESS: Message count is correctly showing 3");
    }
    else
    {
        Console.WriteLine($"❌ FAILURE: Message count is {foundSession?.MessageCount}, expected 3");
    }
}
