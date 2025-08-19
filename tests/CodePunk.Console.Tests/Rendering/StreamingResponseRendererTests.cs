using CodePunk.Console.Rendering;
using CodePunk.Core.Chat;
using CodePunk.Core.Models;
using FluentAssertions;
using Moq;
using Spectre.Console;
using Xunit;

namespace CodePunk.Console.Tests.Rendering;

public class StreamingResponseRendererTests
{
    private readonly StreamingResponseRenderer _renderer;
    private readonly Mock<IAnsiConsole> _mockConsole;

    public StreamingResponseRendererTests()
    {
        _mockConsole = new Mock<IAnsiConsole>();
        _renderer = new StreamingResponseRenderer(_mockConsole.Object);
    }

    [Fact]
    public void Constructor_ShouldInitializeCorrectly()
    {
        // Assert
        _renderer.Should().NotBeNull();
    }

    [Fact]
    public void StartStreaming_ShouldInitializeDisplay()
    {
        // Act & Assert - Should not throw any exceptions
        var action = () => _renderer.StartStreaming();
        action.Should().NotThrow();
    }

    [Fact]
    public void ProcessChunk_ShouldHandleValidContent()
    {
        // Arrange
        var chunk = new ChatStreamChunk { ContentDelta = "Hello", IsComplete = false };

        // Act - Start streaming first
        _renderer.StartStreaming();
        _renderer.ProcessChunk(chunk);

        // Assert - Should not throw
        _renderer.Should().NotBeNull();
    }

    [Fact]
    public void ProcessChunk_ShouldHandleNullContent()
    {
        // Arrange
        var chunk = new ChatStreamChunk { ContentDelta = null, IsComplete = false };

        // Act
        _renderer.StartStreaming();
        _renderer.ProcessChunk(chunk);

        // Assert - Should handle gracefully without throwing
        _renderer.Should().NotBeNull();
    }

    [Fact]
    public void RenderMessage_ShouldHandleUserMessage()
    {
        // Arrange
        var message = Message.Create(
            "session-id",
            MessageRole.User,
            [new TextPart("Hello, AI!")]);

        // Act & Assert - Should not throw any exceptions
        var action = () => _renderer.RenderMessage(message);
        action.Should().NotThrow();
    }

    [Fact]
    public void RenderMessage_ShouldHandleAssistantMessage()
    {
        // Arrange
        var message = Message.Create(
            "session-id",
            MessageRole.Assistant,
            [new TextPart("Hello, human!")],
            "gpt-4",
            "OpenAI");

        // Act & Assert - Should not throw any exceptions
        var action = () => _renderer.RenderMessage(message);
        action.Should().NotThrow();
    }

    [Fact]
    public void CompleteStreaming_ShouldFinishDisplay()
    {
        // Arrange
        _renderer.StartStreaming();

        // Act
        _renderer.CompleteStreaming();

        // Assert - Should complete without errors
        _renderer.Should().NotBeNull();
    }
}
