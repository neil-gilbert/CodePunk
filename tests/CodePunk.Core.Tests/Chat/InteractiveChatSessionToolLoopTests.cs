using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Chat;
using CodePunk.Core.Models;
using CodePunk.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CodePunk.Core.Tests.Chat;

public class InteractiveChatSessionToolLoopTests
{
    private readonly Mock<ISessionService> _sessionService = new();
    private readonly Mock<IMessageService> _messageService = new();
    private readonly Mock<ILLMService> _llmService = new();
    private readonly Mock<IToolService> _toolService = new();
    private readonly Mock<ILogger<InteractiveChatSession>> _logger = new();

    private InteractiveChatSession CreateChatSession(int maxIterations = 3)
    {
        var options = new ChatSessionOptions { MaxToolCallIterations = maxIterations };
        return new InteractiveChatSession(
            _sessionService.Object,
            _messageService.Object,
            _llmService.Object,
            _toolService.Object,
            _logger.Object,
            null,
            options);
    }

    [Fact]
    public async Task SendMessageAsync_ExecutesToolIterationAndResetsToolIteration()
    {
        // Arrange
        var session = Session.Create("Test");
        _sessionService.Setup(s => s.CreateAsync("Test", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _messageService.Setup(m => m.GetBySessionAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Message>());
        _messageService.Setup(m => m.CreateAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Message m, CancellationToken _) => m);

        // First AI response includes a tool call requiring another iteration.
        var firstAiChunks = new List<LLMStreamChunk>
        {
            new() {
                Content = "First",
                ToolCall = new ToolCall {
                    Id = "1",
                    Name = "do_something",
                    Arguments = System.Text.Json.JsonDocument.Parse("{\"x\":1}").RootElement
                },
                IsComplete = true }
        };
        var secondAiChunks = new List<LLMStreamChunk>
        {
            new() { Content = "Second", IsComplete = true }
        };
        int iteration = 0;
        _llmService.Setup(l => l.SendMessageStreamAsync(It.IsAny<IList<Message>>(), It.IsAny<CancellationToken>()))
            .Returns((IList<Message> msgs, CancellationToken ct) =>
            {
                iteration++;
                var chosen = iteration == 1 ? firstAiChunks : secondAiChunks;
                async IAsyncEnumerable<LLMStreamChunk> Stream()
                {
                    foreach (var c in chosen) yield return c;
                    await Task.CompletedTask; // ensure async iterator compliance
                }
                return Stream();
            });

        _toolService.Setup(t => t.ExecuteAsync(It.IsAny<string>(), It.IsAny<System.Text.Json.JsonElement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult { Content = "tool-result" });
        _toolService.Setup(t => t.GetLLMTools()).Returns(new List<LLMTool>());

        var chat = CreateChatSession();
        await chat.StartNewSessionAsync("Test");

        // Act
        var final = await chat.SendMessageAsync("Hello");

        // Assert
        Assert.Equal("Second", final.Parts.OfType<TextPart>().First().Content);
        Assert.Equal(0, chat.ToolIteration); // should be reset after loop
        _messageService.Verify(m => m.CreateAsync(It.Is<Message>(mm => mm.Role == MessageRole.Assistant), It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }
}
