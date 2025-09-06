using CodePunk.Console.Chat;
using CodePunk.Console.Commands;
using CodePunk.Console.Rendering;
using CodePunk.Console.Stores;
using CodePunk.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Spectre.Console;
using System.CommandLine;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;

var builder = Host.CreateApplicationBuilder(args);

// Serilog basic console (can refine later)
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog();

// OpenTelemetry (basic console exporter for now)
builder.Services.AddOpenTelemetry().WithTracing(tp =>
{
    tp.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("CodePunk.CLI"));
    tp.AddSource("CodePunk.CLI");
    tp.AddConsoleExporter();
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
return await root.InvokeAsync(args);
