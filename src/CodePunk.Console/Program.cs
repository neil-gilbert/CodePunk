using CodePunk.Console.Chat;
using CodePunk.Console.Commands;
using CodePunk.Console.Rendering;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Chat;
using CodePunk.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Spectre.Console;

var builder = Host.CreateApplicationBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Services.AddSerilog();

// Add CodePunk services
builder.Services.AddCodePunkServices(builder.Configuration);

// Add console services
builder.Services.AddSingleton<IAnsiConsole>(AnsiConsole.Console);
builder.Services.AddSingleton<StreamingResponseRenderer>();

// Add command services
builder.Services.AddSingleton<HelpCommand>();
builder.Services.AddSingleton<NewCommand>();
builder.Services.AddSingleton<QuitCommand>();
builder.Services.AddSingleton<ClearCommand>();
builder.Services.AddScoped<SessionsCommand>();
builder.Services.AddScoped<LoadCommand>();

builder.Services.AddSingleton<CommandProcessor>(provider =>
{
    var commands = new List<ChatCommand>
    {
        provider.GetRequiredService<HelpCommand>(),
        provider.GetRequiredService<NewCommand>(),
        provider.GetRequiredService<QuitCommand>(),
        provider.GetRequiredService<ClearCommand>(),
        provider.GetRequiredService<SessionsCommand>(),
        provider.GetRequiredService<LoadCommand>()
    };
    
    // Set up help command with all commands
    var helpCommand = commands.OfType<HelpCommand>().First();
    var helpWithCommands = new HelpCommand(commands);
    commands[0] = helpWithCommands;
    
    var logger = provider.GetRequiredService<ILogger<CommandProcessor>>();
    return new CommandProcessor(commands, logger);
});

builder.Services.AddScoped<InteractiveChatLoop>();

var host = builder.Build();

// Ensure database is created
await host.Services.EnsureDatabaseCreatedAsync();

// Run the interactive chat application
try
{
    var chatLoop = host.Services.GetRequiredService<InteractiveChatLoop>();
    await chatLoop.RunAsync();
}
catch (OperationCanceledException)
{
    // Normal shutdown via Ctrl+C
}
catch (Exception ex)
{
    var console = host.Services.GetRequiredService<IAnsiConsole>();
    console.WriteException(ex);
    Environment.Exit(1);
}

await host.StopAsync();
