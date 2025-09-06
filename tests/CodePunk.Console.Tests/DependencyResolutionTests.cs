using System;
using System.CommandLine;
using System.Threading.Tasks;
using CodePunk.Console.Chat;
using CodePunk.Console.Commands;
using CodePunk.Console.Rendering;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CodePunk.Console.Tests;

public class DependencyResolutionTests
{
    [Fact]
    public async Task InteractiveChatLoop_and_RootCommand_should_resolve()
    {
        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
        // Reuse Program.cs registrations (duplicated minimal subset for test)
        // In future consider refactoring to shared method.
        builder.Services.AddLogging();
        builder.Services.AddSingleton<Spectre.Console.IAnsiConsole>(Spectre.Console.AnsiConsole.Console);
        builder.Services.AddSingleton<StreamingResponseRenderer>();
        builder.Services.AddSingleton<InteractiveChatLoop>();
        builder.Services.AddSingleton<CommandProcessor>();
        // Chat commands (may be empty minimal set for resolution)
        builder.Services.AddTransient<ChatCommand, HelpCommand>();
        builder.Services.AddTransient<ChatCommand, NewCommand>();
        builder.Services.AddTransient<ChatCommand, QuitCommand>();
        builder.Services.AddTransient<ChatCommand, ClearCommand>();
        builder.Services.AddTransient<ChatCommand, SessionsCommand>();
        builder.Services.AddTransient<ChatCommand, LoadCommand>();

        using var host = builder.Build();
    var loop = host.Services.GetRequiredService<InteractiveChatLoop>();
        Assert.NotNull(loop);
        var root = RootCommandFactory.Create(host.Services);
        Assert.NotNull(root);
        await Task.CompletedTask;
    }
}
