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

var verbose = Environment.GetEnvironmentVariable("CODEPUNK_VERBOSE") == "1";

var loggerConfig = new LoggerConfiguration();
if (verbose)
{
    loggerConfig = loggerConfig.MinimumLevel.Information()
        .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
}
loggerConfig = loggerConfig.WriteTo.File(
    path: "logs/codepunk-.log",
    rollingInterval: RollingInterval.Day,
    retainedFileCountLimit: 7,
    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

Log.Logger = loggerConfig.CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog(dispose: true);

builder.Services.AddOpenTelemetry().WithTracing(tp =>
{
    tp.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("CodePunk.CLI"));
    tp.AddSource("CodePunk.CLI");
    if (verbose)
    {
        tp.AddConsoleExporter();
    }
});

builder.Services.AddCodePunkServices(builder.Configuration);

builder.Services.AddSingleton<IAnsiConsole>(Spectre.Console.AnsiConsole.Console);
builder.Services.AddSingleton<IAuthStore, AuthFileStore>();
builder.Services.AddSingleton<IAgentStore, AgentFileStore>();
builder.Services.AddSingleton<ISessionFileStore, SessionFileStore>();

builder.Services.AddScoped<InteractiveChatLoop>();
builder.Services.AddSingleton(new StreamingRendererOptions { LiveEnabled = false });
builder.Services.AddSingleton<StreamingResponseRenderer>();

builder.Services.AddTransient<ChatCommand, HelpCommand>();
builder.Services.AddTransient<ChatCommand, NewCommand>();
builder.Services.AddTransient<ChatCommand, QuitCommand>();
builder.Services.AddTransient<ChatCommand, ClearCommand>();
builder.Services.AddTransient<ChatCommand, SessionsCommand>();
builder.Services.AddTransient<ChatCommand, LoadCommand>();
builder.Services.AddTransient<ChatCommand, UseCommand>();
builder.Services.AddTransient<ChatCommand, UsageCommand>();
builder.Services.AddTransient<ChatCommand, ModelsChatCommand>();
builder.Services.AddSingleton<CommandProcessor>();

var host = builder.Build();
await host.Services.EnsureDatabaseCreatedAsync();

var commandProcessor = host.Services.GetRequiredService<CommandProcessor>();
var help = commandProcessor.GetAllCommands().OfType<HelpCommand>().FirstOrDefault();
help?.Initialize(commandProcessor.GetAllCommands());

var root = RootCommandFactory.Create(host.Services);

if (args.Length == 0 && Environment.GetEnvironmentVariable("CODEPUNK_VERBOSE") != "1")
{
    var console = host.Services.GetRequiredService<IAnsiConsole>();
    console.Write(ConsoleStyles.HeaderRule("Interactive Session"));
    console.WriteLine();
    console.MarkupLine(ConsoleStyles.Dim("Type /help for commands."));
    console.WriteLine();
}

return await root.InvokeAsync(args);
