using CodePunk.Console.Chat;
using CodePunk.Console.Commands;
using CodePunk.Console.Rendering;
using CodePunk.Console.Stores;
using CodePunk.Infrastructure.Configuration;
using CodePunk.Console.Themes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Spectre.Console;
using System.CommandLine;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;

var builder = Host.CreateApplicationBuilder(args);

// Quiet / verbose logging control via env var CODEPUNK_VERBOSE=1 (default quiet console)
var verbose = Environment.GetEnvironmentVariable("CODEPUNK_VERBOSE") == "1";

var loggerConfig = new LoggerConfiguration();
if (verbose)
{
    loggerConfig = loggerConfig.MinimumLevel.Information()
        .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
}
// Always write to rolling file (retain existing behavior)
loggerConfig = loggerConfig.WriteTo.File(
    path: "logs/codepunk-.log",
    rollingInterval: RollingInterval.Day,
    retainedFileCountLimit: 7,
    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

Log.Logger = loggerConfig.CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog(dispose: true);

// OpenTelemetry (basic console exporter for now)
builder.Services.AddOpenTelemetry().WithTracing(tp =>
{
    tp.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("CodePunk.CLI"));
    tp.AddSource("CodePunk.CLI");
    if (verbose)
    {
        // Only emit console spans when in verbose mode to keep UI clean
        tp.AddConsoleExporter();
    }
});

// Core + infrastructure (DbContext, repositories, services, providers, tools)
builder.Services.AddCodePunkServices(builder.Configuration);

// CLI-specific persistence stores (file based auth/agent/session index)
builder.Services.AddSingleton<IAnsiConsole>(Spectre.Console.AnsiConsole.Console);
builder.Services.AddSingleton<IAuthStore, AuthFileStore>();
builder.Services.AddSingleton<IAgentStore, AgentFileStore>();
builder.Services.AddSingleton<ISessionFileStore, SessionFileStore>();

// Chat loop orchestration (interactive console loop). Scoped to align with InteractiveChatSession scope.
builder.Services.AddScoped<InteractiveChatLoop>();
// Rendering component for streaming AI responses
builder.Services.AddSingleton<StreamingResponseRenderer>();

// Chat command registrations
builder.Services.AddTransient<ChatCommand, HelpCommand>();
builder.Services.AddTransient<ChatCommand, NewCommand>();
builder.Services.AddTransient<ChatCommand, QuitCommand>();
builder.Services.AddTransient<ChatCommand, ClearCommand>();
builder.Services.AddTransient<ChatCommand, SessionsCommand>();
builder.Services.AddTransient<ChatCommand, LoadCommand>();
builder.Services.AddSingleton<CommandProcessor>();

var host = builder.Build();
await host.Services.EnsureDatabaseCreatedAsync();

// Initialize HelpCommand with full command list (avoid constructor circular dependency)
using (var scope = host.Services.CreateScope())
{
    var commands = scope.ServiceProvider.GetServices<ChatCommand>().ToList();
    var help = commands.OfType<HelpCommand>().FirstOrDefault();
    help?.Initialize(commands);
}

var root = RootCommandFactory.Create(host.Services);

// If no args (interactive) and not verbose logs, render a header rule once for polish
if (args.Length == 0 && Environment.GetEnvironmentVariable("CODEPUNK_VERBOSE") != "1")
{
    var console = host.Services.GetRequiredService<IAnsiConsole>();
    console.Write(ConsoleStyles.HeaderRule("Interactive Session"));
    console.WriteLine();
    console.MarkupLine(ConsoleStyles.Dim("Type /help for commands."));
    console.WriteLine();
}

return await root.InvokeAsync(args);
