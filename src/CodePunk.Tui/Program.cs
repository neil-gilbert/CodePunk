using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RazorConsole.Core;
using CodePunk.Infrastructure.Configuration;
using System.Text.Json;
using CodePunk.Infrastructure.Providers;
using CodePunk.Infrastructure.Settings;
using CodePunk.Core.Abstractions;

namespace CodePunk.Tui;

public static class Program
{
    public static async Task Main(string[] args)
    {
        // Build configuration from the current working directory
        var externalConfiguration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        // Generic host with RazorConsole (v0.1.0+) wired in
        var builder = Host.CreateApplicationBuilder(args);
        builder.Configuration.AddConfiguration(externalConfiguration);

        // Register RazorConsole with the App component as the root
        builder.UseRazorConsole<Components.App>(_ => { });

        // Register CodePunk services and TUI adapters
        builder.Services.AddLogging();
        builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
        builder.Services.AddCodePunkServices(builder.Configuration);
        builder.Services.AddScoped<IApprovalService, CodePunk.Tui.Adapters.RazorApprovalService>();
        builder.Services.AddScoped<CodePunk.Tui.ViewModels.ChatViewModel>();
        builder.Services.AddSingleton<CodePunk.Tui.Services.IApprovalPromptService, CodePunk.Tui.Services.ApprovalPromptService>();

        using var host = builder.Build();

        // Ensure database exists before launching the UI
        await host.Services.EnsureDatabaseCreatedAsync();

        // Register providers from persisted credentials and apply defaults
        try
        {
            var bootstrap = host.Services.GetService<ProviderBootstrapper>();
            if (bootstrap != null)
            {
                await bootstrap.ApplyAsync();
            }

            var defaultsStore = host.Services.GetService<IDefaultsStore>();
            if (defaultsStore != null)
            {
                var defaults = await defaultsStore.LoadAsync();
                var chatSession = host.Services.GetRequiredService<CodePunk.Core.Chat.InteractiveChatSession>();
                if (!string.IsNullOrWhiteSpace(defaults.Provider) || !string.IsNullOrWhiteSpace(defaults.Model))
                {
                    chatSession.UpdateDefaults(defaults.Provider, defaults.Model);
                }
            }
        }
        catch { }

        // Clear console before starting the UI
        System.Console.Clear();

        await host.RunAsync();
    }
}
