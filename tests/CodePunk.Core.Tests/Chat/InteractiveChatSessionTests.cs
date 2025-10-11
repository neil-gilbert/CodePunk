using System.Collections.Concurrent;
using System.Text.Json;
using System.Linq;
using System.Threading.Tasks;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Chat;
using CodePunk.Core.Models;
using CodePunk.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CodePunk.Core.Tests.Chat;

public class InteractiveChatSessionTests
{
    [Fact]
    public async Task SendMessageAsync_StopsAfterRepeatedToolCalls()
    {
        var sessionService = new InMemorySessionService();
        var messageService = new InMemoryMessageService();
        var toolService = new Mock<IToolService>();
        toolService.Setup(s => s.ExecuteAsync(It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult { Content = "ok" });

        var options = new ChatSessionOptions
        {
            MaxToolCallIterations = 4,
            MaxRepeatedToolCalls = 1,
            MaxToolCallsPerIteration = 5,
            MaxConsecutiveToolErrors = 2
        };

        using var toolArgs = JsonDocument.Parse("""{"path":"foo.txt","content":"hello"}""");
        var firstChunk = new LLMStreamChunk
        {
            ToolCall = new ToolCall { Id = "call-1", Name = "write_file", Arguments = toolArgs.RootElement }
        };
        var secondChunk = new LLMStreamChunk
        {
            ToolCall = new ToolCall { Id = "call-2", Name = "write_file", Arguments = toolArgs.RootElement }
        };

        var llmService = new Mock<ILLMService>();
        llmService.SetupSequence(s => s.SendMessageStreamAsync(It.IsAny<IList<Message>>(), It.IsAny<CancellationToken>()))
            .Returns(CreateStream(firstChunk))
            .Returns(CreateStream(secondChunk));

        var chatSession = new InteractiveChatSession(
            sessionService,
            messageService,
            llmService.Object,
            toolService.Object,
            Mock.Of<ILogger<InteractiveChatSession>>(),
            new ChatSessionEventStream(),
            options);

        await chatSession.StartNewSessionAsync("test");

        var finalMessage = await chatSession.SendMessageAsync("run tools");

        var finalText = finalMessage.Parts.OfType<TextPart>().Single().Content;
        finalText.Should().Contain("Stopped tool execution");
        toolService.Verify(s => s.ExecuteAsync(It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_BlocksWhenTooManyToolsRequested()
    {
        var sessionService = new InMemorySessionService();
        var messageService = new InMemoryMessageService();
        var toolService = new Mock<IToolService>();

        var options = new ChatSessionOptions
        {
            MaxToolCallIterations = 4,
            MaxToolCallsPerIteration = 3,
            MaxRepeatedToolCalls = 2,
            MaxConsecutiveToolErrors = 2
        };

        using var toolArgs = JsonDocument.Parse("""{"path":"foo.txt","content":"hello"}""");
        var toolCalls = Enumerable.Range(0, 4)
            .Select(i => new LLMStreamChunk
            {
                ToolCall = new ToolCall { Id = $"call-{i}", Name = "write_file", Arguments = toolArgs.RootElement }
            })
            .ToArray();

        var llmService = new Mock<ILLMService>();
        llmService.Setup(s => s.SendMessageStreamAsync(It.IsAny<IList<Message>>(), It.IsAny<CancellationToken>()))
            .Returns(CreateStream(toolCalls));

        var chatSession = new InteractiveChatSession(
            sessionService,
            messageService,
            llmService.Object,
            toolService.Object,
            Mock.Of<ILogger<InteractiveChatSession>>(),
            new ChatSessionEventStream(),
            options);

        await chatSession.StartNewSessionAsync("test");

        var finalMessage = await chatSession.SendMessageAsync("run too many tools");

        var finalText = finalMessage.Parts.OfType<TextPart>().Single().Content;
        finalText.Should().Contain("requested 4 tool commands");
        toolService.Verify(s => s.ExecuteAsync(It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class InMemorySessionService : ISessionService
    {
        private readonly ConcurrentDictionary<string, Session> _sessions = new();

        public Task<Session?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_sessions.TryGetValue(id, out var session) ? session : null);

        public Task<IReadOnlyList<Session>> GetRecentAsync(int count = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Session>>(_sessions.Values.Take(count).ToList());

        public Task<Session> CreateAsync(string title, string? parentSessionId = null, CancellationToken cancellationToken = default)
        {
            var session = Session.Create(title, parentSessionId);
            _sessions[session.Id] = session;
            return Task.FromResult(session);
        }

        public Task<Session> UpdateAsync(Session session, CancellationToken cancellationToken = default)
        {
            _sessions[session.Id] = session;
            return Task.FromResult(session);
        }

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            _sessions.TryRemove(id, out _);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryMessageService : IMessageService
    {
        private readonly ConcurrentDictionary<string, List<Message>> _messages = new();

        public Task<Message?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            var message = _messages.Values.SelectMany(x => x).FirstOrDefault(m => m.Id == id);
            return Task.FromResult(message);
        }

        public Task<IReadOnlyList<Message>> GetBySessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            var messages = _messages.GetOrAdd(sessionId, _ => new List<Message>());
            return Task.FromResult<IReadOnlyList<Message>>(messages.ToList());
        }

        public Task<Message> CreateAsync(Message message, CancellationToken cancellationToken = default)
        {
            var messages = _messages.GetOrAdd(message.SessionId, _ => new List<Message>());
            messages.Add(message);
            return Task.FromResult(message);
        }

        public Task<Message> UpdateAsync(Message message, CancellationToken cancellationToken = default)
        {
            var messages = _messages.GetOrAdd(message.SessionId, _ => new List<Message>());
            var index = messages.FindIndex(m => m.Id == message.Id);
            if (index >= 0)
            {
                messages[index] = message;
            }
            return Task.FromResult(message);
        }

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            foreach (var entry in _messages.Values)
            {
                var index = entry.FindIndex(m => m.Id == id);
                if (index >= 0)
                {
                    entry.RemoveAt(index);
                    break;
                }
            }

            return Task.CompletedTask;
        }

        public Task DeleteBySessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            _messages.TryRemove(sessionId, out _);
            return Task.CompletedTask;
        }
    }

    private static async IAsyncEnumerable<LLMStreamChunk> CreateStream(params LLMStreamChunk[] chunks)
    {
        foreach (var chunk in chunks)
        {
            yield return chunk;
            await Task.Yield();
        }
    }
}
