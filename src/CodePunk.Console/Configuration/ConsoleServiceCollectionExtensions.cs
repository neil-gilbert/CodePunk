using CodePunk.Console.Chat;
using CodePunk.Console.Commands;
using CodePunk.Console.Rendering;
using CodePunk.Console.Stores;
using CodePunk.Infrastructure.Settings;
using CodePunk.Core.Chat;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Microsoft.Extensions.Configuration;
using CodePunk.Console.Planning;
using Microsoft.Extensions.Logging;
using CodePunk.Core.SyntaxHighlighting.Abstractions;
using CodePunk.Core.Abstractions;

namespace CodePunk.Console.Configuration;

public static class ConsoleServiceCollectionExtensions
{
    public static IServiceCollection AddCodePunkConsole(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddSingleton<IAnsiConsole>(AnsiConsole.Console);
        // Auth/defaults stores now provided by Infrastructure
        services.AddSingleton<IAgentStore, AgentFileStore>();
        services.AddSingleton<ISessionFileStore, SessionFileStore>();
        services.AddSingleton<IPlanFileStore, PlanFileStore>();
        if (configuration != null)
        {
            services.Configure<PlanAiGenerationOptions>(configuration.GetSection("PlanAI"));
        }
        services.AddScoped<Planning.IPlanAiGenerationService, Planning.PlanAiGenerationService>();
        services.AddScoped<CodePunk.Core.Abstractions.IPlanService, Console.Planning.ConsolePlanServiceAdapter>();

        services.AddScoped<InteractiveChatLoop>();
        services.AddSingleton(new StreamingRendererOptions { LiveEnabled = false });
        services.AddSingleton<StreamingResponseRenderer>(sp =>
        {
            var console = sp.GetRequiredService<IAnsiConsole>();
            var options = sp.GetRequiredService<StreamingRendererOptions>();
            var highlighter = sp.GetService<ISyntaxHighlighter>();
            return new StreamingResponseRenderer(console, options, highlighter);
        });

        services.AddSingleton<DiffRenderer>(sp =>
        {
            var highlighter = sp.GetService<ISyntaxHighlighter>();
            return new DiffRenderer(highlighter);
        });

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
        services.AddScoped<IApprovalService, CodePunk.Console.Adapters.SpectreApprovalService>();
        services.AddSingleton<CommandProcessor>();
        
        return services;
    }
}
