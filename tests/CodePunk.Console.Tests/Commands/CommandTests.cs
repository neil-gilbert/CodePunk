using CodePunk.Console.Commands;
using CodePunk.Core.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CodePunk.Console.Tests.Commands;

public class CommandProcessorTests
{
    private readonly CommandProcessor _commandProcessor;

    public CommandProcessorTests()
    {
        var commands = new ChatCommand[]
        {
            new HelpCommand(),
            new NewCommand(),
            new QuitCommand(),
            new ClearCommand(),
            new SessionsCommand(Mock.Of<ISessionService>(), Mock.Of<IMessageService>()),
            new LoadCommand(Mock.Of<ISessionService>())
        };
        _commandProcessor = new CommandProcessor(commands, NullLogger<CommandProcessor>.Instance);
    }

    [Theory]
    [InlineData("/help", true)]
    [InlineData("/new", true)]
    [InlineData("/quit", true)]
    [InlineData("/clear", true)]
    [InlineData("/sessions", true)]
    [InlineData("/load session-id", true)]
    [InlineData("regular message", false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    public void IsCommand_ShouldCorrectlyIdentifyCommands(string input, bool expectedIsCommand)
    {
        // Act
        var result = _commandProcessor.IsCommand(input);

        // Assert
        result.Should().Be(expectedIsCommand);
    }

    [Fact]
    public async Task ExecuteCommandAsync_ShouldExecuteHelpCommand()
    {
        // Act
        var result = await _commandProcessor.ExecuteCommandAsync("/help");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
    result.Message.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteCommandAsync_ShouldExecuteQuitCommand()
    {
        // Act
        var result = await _commandProcessor.ExecuteCommandAsync("/quit");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ShouldExit.Should().BeTrue();
        result.Message.Should().Contain("Goodbye");
    }

    [Fact]
    public async Task ExecuteCommandAsync_ShouldReturnError_ForUnknownCommand()
    {
        // Act
        var result = await _commandProcessor.ExecuteCommandAsync("/unknown");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Unknown command");
    }

    [Fact]
    public async Task ExecuteCommandAsync_ShouldReturnError_ForNonCommands()
    {
        // Act
        var result = await _commandProcessor.ExecuteCommandAsync("not a command");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Invalid command format");
    }
}

public class ChatCommandTests
{
    [Fact]
    public void CommandResult_ShouldInitializeCorrectly()
    {
        // Act
        var successResult = CommandResult.Ok("Success message");
        var errorResult = CommandResult.Error("Error message");
        var exitResult = CommandResult.Exit("Exit message");

        // Assert
        successResult.Success.Should().BeTrue();
        successResult.ShouldExit.Should().BeFalse();
        successResult.Message.Should().Be("Success message");

        errorResult.Success.Should().BeFalse();
        errorResult.ShouldExit.Should().BeFalse();
        errorResult.Message.Should().Be("Error message");

        exitResult.Success.Should().BeTrue();
        exitResult.ShouldExit.Should().BeTrue();
        exitResult.Message.Should().Be("Exit message");
    }

    [Fact]
    public async Task HelpCommand_ShouldReturnHelpMessage()
    {
        // Arrange
        var helpCommand = new HelpCommand();

        // Act
        var result = await helpCommand.ExecuteAsync([]);

        // Assert
        result.Success.Should().BeTrue();
        result.Message.Should().BeNull();
    }

    [Fact]
    public async Task QuitCommand_ShouldReturnExitResult()
    {
        // Arrange
        var quitCommand = new QuitCommand();

        // Act
        var result = await quitCommand.ExecuteAsync([]);

        // Assert
        result.Success.Should().BeTrue();
        result.ShouldExit.Should().BeTrue();
        result.Message.Should().Contain("Goodbye");
    }
}
