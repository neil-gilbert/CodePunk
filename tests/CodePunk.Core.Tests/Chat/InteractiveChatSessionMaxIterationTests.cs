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

public class InteractiveChatSessionMaxIterationTests
{
	private readonly Mock<ISessionService> _sessionService = new();
	private readonly Mock<IMessageService> _messageService = new();
	private readonly Mock<ILLMService> _llmService = new();
	private readonly Mock<IToolService> _toolService = new();
	private readonly Mock<ILogger<InteractiveChatSession>> _logger = new();

	private InteractiveChatSession Create(int maxIterations)
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
	public async Task SendMessageAsync_WhenMaxIterationsExceeded_UsesFallbackAndResetsIteration()
	{
		var session = Session.Create("LoopTest");
		_sessionService.Setup(s => s.CreateAsync("LoopTest", null, It.IsAny<CancellationToken>()))
			.ReturnsAsync(session);
		_messageService.Setup(m => m.GetBySessionAsync(session.Id, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new List<Message>());
		_messageService.Setup(m => m.CreateAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync((Message m, CancellationToken _) => m);

		var toolCallChunk = new LLMStreamChunk
		{
			Content = "Tool",
			ToolCall = new ToolCall
			{
				Id = "x",
				Name = "do",
				Arguments = System.Text.Json.JsonDocument.Parse("{\"a\":1}").RootElement
			},
			IsComplete = true
		};
		_llmService.Setup(l => l.SendMessageStreamAsync(It.IsAny<IList<Message>>(), It.IsAny<CancellationToken>()))
			.Returns((IList<Message> _, CancellationToken __) =>
			{
				async IAsyncEnumerable<LLMStreamChunk> Stream()
				{
					// ensure async state machine (compiler requires 'async' for IAsyncEnumerable with yield)
					yield return toolCallChunk;
					await Task.CompletedTask; // keep method async even with single chunk
				}
				return Stream();
			});

		_toolService.Setup(t => t.ExecuteAsync(It.IsAny<string>(), It.IsAny<System.Text.Json.JsonElement>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ToolResult { Content = "ok" });
		_toolService.Setup(t => t.GetLLMTools()).Returns(new List<LLMTool>());

		var chat = Create(maxIterations: 2);
		await chat.StartNewSessionAsync("LoopTest");
		var final = await chat.SendMessageAsync("Hi");

		Assert.Contains("too many tool calls", final.Parts.OfType<TextPart>().First().Content);
		Assert.Equal(0, chat.ToolIteration);
		_messageService.Verify(m => m.CreateAsync(It.Is<Message>(mm => mm.Role == MessageRole.Assistant), It.IsAny<CancellationToken>()), Times.AtLeast(1));
	}
}
