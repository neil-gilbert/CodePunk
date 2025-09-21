using CodePunk.Console.Chat;
using CodePunk.Console.Commands;
using CodePunk.Console.Rendering;
using CodePunk.Console.Configuration;
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
using CodePunk.Core.Chat;
using CodePunk.Console.Stores;

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
    tp.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("CodePunk"));
    tp.AddSource("CodePunk");
    if (verbose)
    {
        tp.AddConsoleExporter();
    }
});

builder.Services.AddCodePunkServices(builder.Configuration);

builder.Services.AddCodePunkConsole(builder.Configuration);

var host = builder.Build();
await host.Services.EnsureDatabaseCreatedAsync();

// Dynamic provider bootstrap (register providers from env/auth before commands run)
try
{
    var bootstrap = host.Services.GetService<CodePunk.Console.Providers.ProviderBootstrap>();
    if (bootstrap != null)
    {
        await bootstrap.ApplyAsync();
    }
}
catch (Exception ex)
{
    // Non-fatal; proceed without providers if bootstrap fails
    Log.Logger.Warning(ex, "Provider bootstrap failed");
}

var commandProcessor = host.Services.GetRequiredService<CommandProcessor>();

var root = RootCommandFactory.Create(host.Services);

var consoleService = host.Services.GetRequiredService<IAnsiConsole>();
// Apply persisted defaults if available
try
{
    var defaultsStore = host.Services.GetService<CodePunk.Console.Stores.IDefaultsStore>();
    if (defaultsStore != null)
    {
        var defaults = await defaultsStore.LoadAsync();
        if (!string.IsNullOrWhiteSpace(defaults.Provider) || !string.IsNullOrWhiteSpace(defaults.Model))
        {
            var session = host.Services.GetRequiredService<InteractiveChatSession>();
            session.UpdateDefaults(defaults.Provider, defaults.Model);
        }
    }
}
catch { }
if (args.Length == 0 && Environment.GetEnvironmentVariable("CODEPUNK_VERBOSE") != "1")
{
    consoleService.Write(ConsoleStyles.HeaderRule("Interactive Session"));
    consoleService.WriteLine();
    consoleService.MarkupLine(ConsoleStyles.Dim("Type /help for commands."));
    consoleService.WriteLine();
}

// Custom root help rendering
if (args.Length > 0 && args.All(a => a == "--help" || a == "-h"))
{
    return HelpRenderer.ShowRootHelp(consoleService, root);
}

return await root.InvokeAsync(args);
