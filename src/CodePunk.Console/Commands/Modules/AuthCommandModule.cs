using System.CommandLine;
using System.Diagnostics;
using Spectre.Console;
using CodePunk.Infrastructure.Settings;
using Microsoft.Extensions.DependencyInjection;
using CodePunk.Console.Themes;
using CodePunk.Console.Stores;
using CodePunk.Core.Abstractions;

namespace CodePunk.Console.Commands.Modules;

internal sealed class AuthCommandModule : ICommandModule
{
    public void Register(RootCommand root, IServiceProvider services)
    {
        root.Add(BuildAuth(services));
    }
    private static Command BuildAuth(IServiceProvider services)
    {
        var auth = new Command("auth", "Manage provider credentials");
        var providerOpt = new Option<string>("--provider", description: "Provider name") { IsRequired = true };
        var keyOpt = new Option<string>("--key", () => string.Empty, "API key (omit to prompt)");
        var login = new Command("login", "Store an API key") { providerOpt, keyOpt };
        login.SetHandler(async (string provider, string key) =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("auth.login", ActivityKind.Client);
            activity?.SetTag("provider", provider);
            var store = services.GetRequiredService<IAuthStore>();
            var console = services.GetRequiredService<IAnsiConsole>();
            if (string.IsNullOrWhiteSpace(key))
            {
                key = console.Prompt(new TextPrompt<string>(ConsoleStyles.Accent("Enter API key:"))
                    .PromptStyle("silver")
                    .Secret());
            }
            await store.SetAsync(provider, key);
            if (!Rendering.OutputContext.IsQuiet()) console.MarkupLine($"{ConsoleStyles.Success("Stored")} {ConsoleStyles.Dim("provider")} {ConsoleStyles.Accent(provider)}");
        }, providerOpt, keyOpt);
        var list = new Command("list", "List authenticated providers");
        list.SetHandler(async () =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("auth.list", ActivityKind.Client);
            var store = services.GetRequiredService<IAuthStore>();
            var map = await store.LoadAsync();
            var console = services.GetRequiredService<IAnsiConsole>();
            if (map.Count == 0) { if (!Rendering.OutputContext.IsQuiet()) console.MarkupLine(ConsoleStyles.Warn("No providers authenticated.")); return; }
            var table = new Table().RoundedBorder().Title(ConsoleStyles.PanelTitle("Providers")).AddColumn("Name").AddColumn("Key");
            foreach (var kv in map)
            {
                var masked = kv.Value.Length <= 8 ? new string('*', kv.Value.Length) : kv.Value[..4] + new string('*', kv.Value.Length-4);
                table.AddRow(ConsoleStyles.Accent(kv.Key), $"[grey]{masked}[/]");
            }
            console.Write(table);
        });
        var logoutProviderOpt = new Option<string>("--provider") { IsRequired = true };
        var logout = new Command("logout", "Remove stored provider key") { logoutProviderOpt };
        logout.SetHandler(async (string provider) =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("auth.logout", ActivityKind.Client);
            activity?.SetTag("provider", provider);
            var store = services.GetRequiredService<IAuthStore>();
            await store.RemoveAsync(provider);
            var console = services.GetRequiredService<IAnsiConsole>();
            if (!Rendering.OutputContext.IsQuiet()) console.MarkupLine($"{ConsoleStyles.Success("Removed")} {ConsoleStyles.Accent(provider)}");
        }, logoutProviderOpt);
        auth.AddCommand(login); auth.AddCommand(list); auth.AddCommand(logout); return auth;
    }
}
