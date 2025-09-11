using CodePunk.Console.Chat;
using CodePunk.Console.Commands;
using CodePunk.Console.Rendering;
using CodePunk.Console.Stores;
using CodePunk.Core.Chat;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace CodePunk.Console.Configuration;

public static class ConsoleServiceCollectionExtensions
{
    public static IServiceCollection AddCodePunkConsole(this IServiceCollection services)
    {
        services.AddSingleton<IAnsiConsole>(AnsiConsole.Console);
        services.AddSingleton<IAuthStore, AuthFileStore>();
        services.AddSingleton<IAgentStore, AgentFileStore>();
        services.AddSingleton<ISessionFileStore, SessionFileStore>();
    services.AddSingleton<IPlanFileStore, PlanFileStore>();

        services.AddScoped<InteractiveChatLoop>();
        services.AddSingleton(new StreamingRendererOptions { LiveEnabled = false });
        services.AddSingleton<StreamingResponseRenderer>();

        services.AddTransient<ChatCommand, HelpCommand>();
        services.AddTransient<ChatCommand, NewCommand>();
        services.AddTransient<ChatCommand, QuitCommand>();
        services.AddTransient<ChatCommand, ClearCommand>();
        services.AddTransient<ChatCommand, SessionsCommand>();
        services.AddTransient<ChatCommand, LoadCommand>();
        services.AddTransient<ChatCommand, UseCommand>();
        services.AddTransient<ChatCommand, UsageCommand>();
        services.AddTransient<ChatCommand, ModelsChatCommand>();
        services.AddSingleton<CommandProcessor>();
        return services;
    }
}