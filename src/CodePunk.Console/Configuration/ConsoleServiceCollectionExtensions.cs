using CodePunk.Console.Chat;
using CodePunk.Console.Commands;
using CodePunk.Console.Rendering;
using CodePunk.Console.Stores;
using CodePunk.Core.Chat;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Microsoft.Extensions.Configuration;
using CodePunk.Console.Planning;
using CodePunk.Console.Providers;
using Microsoft.Extensions.Logging;

namespace CodePunk.Console.Configuration;

public static class ConsoleServiceCollectionExtensions
{
    public static IServiceCollection AddCodePunkConsole(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddSingleton<IAnsiConsole>(AnsiConsole.Console);
        services.AddSingleton<IAuthStore, AuthFileStore>();
        services.AddSingleton<IAgentStore, AgentFileStore>();
        services.AddSingleton<ISessionFileStore, SessionFileStore>();
        services.AddSingleton<IPlanFileStore, PlanFileStore>();
        services.AddSingleton<IDefaultsStore, DefaultsFileStore>();
        services.AddSingleton<ProviderBootstrap>(sp => new ProviderBootstrap(services, configuration!, sp.GetRequiredService<IAuthStore>(), sp.GetRequiredService<ILogger<ProviderBootstrap>>()));
        if (configuration != null)
        {
            services.Configure<PlanAiGenerationOptions>(configuration.GetSection("PlanAI"));
        }
        services.AddScoped<Planning.IPlanAiGenerationService, Planning.PlanAiGenerationService>();

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
        services.AddTransient<ChatCommand, PlanChatCommand>();
        services.AddTransient<ChatCommand, SetupCommand>();
        services.AddTransient<ChatCommand, ReloadCommand>();
        services.AddTransient<ChatCommand, ProvidersCommand>();
        services.AddTransient<ChatCommand, CheckpointsCommand>();
        services.AddTransient<ChatCommand, RestoreCommand>();
        services.AddSingleton<CommandProcessor>();
        
        return services;
    }
}