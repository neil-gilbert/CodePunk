using System.Reflection;
using System.Text.Json;
using CodePunk.Core.Configuration;
using CodePunk.Core.Tools;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CodePunk.Core.Tests.Tools;

public class ShellToolTests
{
    [Fact]
    public async Task ExecuteAsync_BlocksLikelyInteractiveCommands()
    {
        var tool = new ShellTool(Options.Create(new ShellCommandOptions()));

        using var doc = JsonDocument.Parse("""{"command":"rails new blog"}""");
        var result = await tool.ExecuteAsync(doc.RootElement.Clone());

        result.IsError.Should().BeTrue();
        result.ErrorMessage.Should().Be("INTERACTIVE_COMMAND_BLOCKED");
        result.Content.Should().Contain("Command blocked");
    }

    [Theory]
    [InlineData("npm create next-app --yes")]
    [InlineData("dotnet new webapi --force")]
    [InlineData("rails new blog --skip-bundle")]
    public void IsLikelyInteractiveCommand_AllowsWhenNonInteractiveFlagsPresent(string command)
    {
        var method = typeof(ShellTool).GetMethod("IsLikelyInteractiveCommand", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var args = new object?[] { command, null };
        var isInteractive = (bool)method!.Invoke(null, args)!;

        isInteractive.Should().BeFalse($"command '{command}' should be treated as non-interactive");
        args[1].Should().BeOfType<string>();
        ((string)args[1]!).Should().BeEmpty();
    }

    [Fact]
    public void TryRewriteInteractiveCommand_AddsYesForCreateNextApp()
    {
        var method = typeof(ShellTool).GetMethod("TryRewriteInteractiveCommand", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var args = new object?[] { "npx create-next-app@latest my-app --typescript", null, null };
        var rewritten = (bool)method!.Invoke(null, args)!;

        rewritten.Should().BeTrue();
        args[1].Should().BeOfType<string>();
        args[2].Should().BeOfType<string>();
        ((string)args[1]!).Should().Contain("--yes");
        ((string)args[2]!).Should().Contain("--yes");
    }

    [Fact]
    public void TryRewriteInteractiveCommand_AddsForceForDotnetNew()
    {
        var method = typeof(ShellTool).GetMethod("TryRewriteInteractiveCommand", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var args = new object?[] { "dotnet new console", null, null };
        var rewritten = (bool)method!.Invoke(null, args)!;

        rewritten.Should().BeTrue();
        ((string)args[1]!).Should().Contain("--force");
        ((string)args[1]!).Should().Contain("--no-restore");
        ((string)args[2]!).Should().Contain("--force");
    }
}
