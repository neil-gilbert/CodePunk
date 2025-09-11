using CodePunk.Core.Abstractions;
using CodePunk.Core.Chat;
using CodePunk.Core.Models;
using CodePunk.Core.Services;
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CodePunk.Core.Tests.Chat;

public class InteractiveChatSessionTests : IDisposable
{
    private readonly Mock<ISessionService> _mockSessionService;
    private readonly Mock<IMessageService> _mockMessageService;
    private readonly Mock<ILLMService> _mockLLMService;
    private readonly ChatSessionEventStream _eventStream;
    private readonly InteractiveChatSession _chatSession;

    public InteractiveChatSessionTests()
    {
        _mockSessionService = new Mock<ISessionService>();
        _mockMessageService = new Mock<IMessageService>();
        _mockLLMService = new Mock<ILLMService>();
        var mockToolService = new Mock<IToolService>();
        
        _eventStream = new ChatSessionEventStream();
        _chatSession = new InteractiveChatSession(
            _mockSessionService.Object,
            _mockMessageService.Object,
            _mockLLMService.Object,
            mockToolService.Object,
            NullLogger<InteractiveChatSession>.Instance,
            _eventStream);
    }

    [Fact]
    public void Constructor_ShouldInitializeCorrectly()
    {
        // Assert
        _chatSession.IsActive.Should().BeFalse();
        _chatSession.IsProcessing.Should().BeFalse();
        _chatSession.CurrentSession.Should().BeNull();
    }

    [Fact]
    public async Task StartNewSessionAsync_ShouldCreateAndSetCurrentSession()
    {
        // Arrange
        const string title = "Test Chat Session";
        var expectedSession = Session.Create(title);
        
        _mockSessionService
            .Setup(s => s.CreateAsync(title, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedSession);

        // Act
        var result = await _chatSession.StartNewSessionAsync(title);

        // Assert
        result.Should().BeEquivalentTo(expectedSession);
        _chatSession.CurrentSession.Should().BeEquivalentTo(expectedSession);
        _chatSession.IsActive.Should().BeTrue();
        
        _mockSessionService.Verify(s => s.CreateAsync(title, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoadSessionAsync_ShouldLoadExistingSession_WhenSessionExists()
    {
        // Arrange
        const string sessionId = "test-session-id";
        var existingSession = Session.Create("Existing Session");
        existingSession.GetType().GetProperty("Id")?.SetValue(existingSession, sessionId);
        
        _mockSessionService
            .Setup(s => s.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSession);

        // Act
        var result = await _chatSession.LoadSessionAsync(sessionId);

        // Assert
        result.Should().BeTrue();
        _chatSession.CurrentSession.Should().BeEquivalentTo(existingSession);
        _chatSession.IsActive.Should().BeTrue();
        
        _mockSessionService.Verify(s => s.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoadSessionAsync_ShouldReturnFalse_WhenSessionDoesNotExist()
    {
        // Arrange
        const string sessionId = "non-existent-session";
        
        _mockSessionService
            .Setup(s => s.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Session?)null);

        // Act
        var result = await _chatSession.LoadSessionAsync(sessionId);

        // Assert
        result.Should().BeFalse();
        _chatSession.CurrentSession.Should().BeNull();
        _chatSession.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task SendMessageAsync_ShouldThrowException_WhenNoActiveSession()
    {
        // Act & Assert
        await _chatSession.Invoking(cs => cs.SendMessageAsync("Hello"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("No active session. Start a new session first.");
    }

    [Fact]
    public async Task SendMessageAsync_ShouldCreateUserMessageAndGetAIResponse()
    {
        // Arrange
        await SetupActiveSessionAsync();
        const string userContent = "Hello, AI!";
        const string aiContent = "Hello, human!";
        
        var mockMessages = new List<Message>();
        var streamChunks = new List<LLMStreamChunk>
        {
            new() { Content = aiContent, IsComplete = true }
        };

        _mockMessageService
            .Setup(m => m.CreateAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Message m, CancellationToken _) => m);
            
        _mockMessageService
            .Setup(m => m.GetBySessionAsync(_chatSession.CurrentSession!.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockMessages);
            
        _mockLLMService
            .Setup(l => l.SendMessageStreamAsync(It.IsAny<IList<Message>>(), It.IsAny<CancellationToken>()))
            .Returns(streamChunks.ToAsyncEnumerable());

        // Act
        var result = await _chatSession.SendMessageAsync(userContent);

        // Assert
        result.Should().NotBeNull();
        result.Role.Should().Be(MessageRole.Assistant);
        result.Parts.OfType<TextPart>().First().Content.Should().Be(aiContent);
        _chatSession.IsProcessing.Should().BeFalse();
        
        // Verify user message was saved
        _mockMessageService.Verify(m => m.CreateAsync(
            It.Is<Message>(msg => 
                msg.Role == MessageRole.User && 
                msg.Parts.OfType<TextPart>().First().Content == userContent),
            It.IsAny<CancellationToken>()), Times.Once);
            
        // Verify AI response was saved
        _mockMessageService.Verify(m => m.CreateAsync(
            It.Is<Message>(msg => 
                msg.Role == MessageRole.Assistant &&
                msg.Parts.OfType<TextPart>().First().Content == aiContent),
            It.IsAny<CancellationToken>()), Times.Once);
        
        // Verify LLM service was called
        _mockLLMService.Verify(l => l.SendMessageStreamAsync(It.IsAny<IList<Message>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendMessageStreamAsync_ShouldThrowException_WhenNoActiveSession()
    {
        // Act & Assert
        await _chatSession.Invoking(async cs =>
        {
            await foreach (var chunk in cs.SendMessageStreamAsync("Hello"))
            {
                // This should not execute
            }
        }).Should().ThrowAsync<InvalidOperationException>()
          .WithMessage("No active session. Start a new session first.");
    }

    [Fact]
    public async Task SendMessageStreamAsync_ShouldStreamResponseChunks()
    {
        // Arrange
        await SetupActiveSessionAsync();
        const string userContent = "Hello, AI!";
        
        var streamChunks = new List<LLMStreamChunk>
        {
            new() { Content = "Hello", IsComplete = false },
            new() { Content = " there", IsComplete = false },
            new() { Content = "!", IsComplete = true }
        };
        
        var mockMessages = new List<Message>();

        _mockMessageService
            .Setup(m => m.CreateAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Message m, CancellationToken _) => m);
            
        _mockMessageService
            .Setup(m => m.GetBySessionAsync(_chatSession.CurrentSession!.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockMessages);
            
        _mockLLMService
            .Setup(l => l.SendMessageStreamAsync(It.IsAny<IList<Message>>(), It.IsAny<CancellationToken>()))
            .Returns(streamChunks.ToAsyncEnumerable());

        // Act
        var receivedChunks = new List<ChatStreamChunk>();
        await foreach (var chunk in _chatSession.SendMessageStreamAsync(userContent))
        {
            receivedChunks.Add(chunk);
        }

        // Assert
        receivedChunks.Should().HaveCount(3);
        receivedChunks[0].ContentDelta.Should().Be("Hello");
        receivedChunks[1].ContentDelta.Should().Be(" there");
        receivedChunks[2].ContentDelta.Should().Be("!");
        receivedChunks[2].IsComplete.Should().BeTrue();
        
        _chatSession.IsProcessing.Should().BeFalse();
        
        // Verify user message was saved
        _mockMessageService.Verify(m => m.CreateAsync(
            It.Is<Message>(msg => msg.Role == MessageRole.User),
            It.IsAny<CancellationToken>()), Times.Once);
            
        // Verify complete AI response was saved
        _mockMessageService.Verify(m => m.CreateAsync(
            It.Is<Message>(msg => 
                msg.Role == MessageRole.Assistant &&
                msg.Parts.OfType<TextPart>().First().Content == "Hello there!"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetConversationHistoryAsync_ShouldReturnEmptyList_WhenNoActiveSession()
    {
        // Act
        var result = await _chatSession.GetConversationHistoryAsync();

        // Assert
        result.Should().BeEmpty();
        _mockMessageService.Verify(m => m.GetBySessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetConversationHistoryAsync_ShouldReturnMessages_WhenActiveSession()
    {
        // Arrange
        await SetupActiveSessionAsync();
        var expectedMessages = new List<Message>
        {
            Message.Create(_chatSession.CurrentSession!.Id, MessageRole.User, [new TextPart("Hello")]),
            Message.Create(_chatSession.CurrentSession.Id, MessageRole.Assistant, [new TextPart("Hi there!")])
        };

        _mockMessageService
            .Setup(m => m.GetBySessionAsync(_chatSession.CurrentSession.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedMessages);

        // Act
        var result = await _chatSession.GetConversationHistoryAsync();

        // Assert
        result.Should().BeEquivalentTo(expectedMessages);
        _mockMessageService.Verify(m => m.GetBySessionAsync(_chatSession.CurrentSession.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ClearSession_ShouldResetSessionState()
    {
        // Arrange
        var session = Session.Create("Test Session");
        _chatSession.GetType().GetProperty("CurrentSession")?.SetValue(_chatSession, session);

        // Act
        _chatSession.ClearSession();

        // Assert
        _chatSession.CurrentSession.Should().BeNull();
        _chatSession.IsActive.Should().BeFalse();
        _chatSession.IsProcessing.Should().BeFalse();
    }

    [Fact]
    public async Task SendMessageAsync_ShouldSetProcessingState_DuringExecution()
    {
        // Arrange
        await SetupActiveSessionAsync();
        var processingStates = new List<bool>();
        
        var streamChunks = new List<LLMStreamChunk>
        {
            new() { Content = "Response", IsComplete = true }
        };
        
        _mockLLMService
            .Setup(l => l.SendMessageStreamAsync(It.IsAny<IList<Message>>(), It.IsAny<CancellationToken>()))
            .Callback(() => processingStates.Add(_chatSession.IsProcessing))
            .Returns(streamChunks.ToAsyncEnumerable());

        _mockMessageService
            .Setup(m => m.CreateAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Message m, CancellationToken _) => m);
            
        _mockMessageService
            .Setup(m => m.GetBySessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Message>());

        // Act
        _chatSession.IsProcessing.Should().BeFalse(); // Before
        var task = _chatSession.SendMessageAsync("Test");
        _chatSession.IsProcessing.Should().BeTrue(); // During
        await task;
        _chatSession.IsProcessing.Should().BeFalse(); // After

        // Assert
        processingStates.Should().ContainSingle().Which.Should().BeTrue();
    }

    [Fact]
    public async Task SendMessageAsync_ShouldEmit_MessageStart_Then_MessageComplete()
    {
        await SetupActiveSessionAsync();
        var chunks = new[]{ new LLMStreamChunk { Content = "Hi", IsComplete = true } };
        _mockMessageService.Setup(m=>m.CreateAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>())).ReturnsAsync((Message m, CancellationToken _) => m);
        _mockMessageService.Setup(m=>m.GetBySessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<Message>());
        _mockLLMService.Setup(l=>l.SendMessageStreamAsync(It.IsAny<IList<Message>>(), It.IsAny<CancellationToken>())).Returns(chunks.ToAsyncEnumerable());

        var read = CollectEventsAsync(_eventStream.Reader, count:2, timeout:TimeSpan.FromSeconds(2));
        var _ = await _chatSession.SendMessageAsync("Hello");
        var events = await read;
        events.Should().HaveCountGreaterOrEqualTo(2);
        events[0].Type.Should().Be(ChatSessionEventType.MessageStart);
        events.Last().Type.Should().Be(ChatSessionEventType.MessageComplete);
    }

    [Fact]
    public async Task SendMessageStreamAsync_ShouldEmit_StreamDelta_And_ToolIterationEvents()
    {
        await SetupActiveSessionAsync();
        var chunks = new[]{
            new LLMStreamChunk { Content = "Part1", IsComplete = false },
            new LLMStreamChunk { Content = "Part2", IsComplete = true }
        };
        _mockMessageService.Setup(m=>m.CreateAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>())).ReturnsAsync((Message m, CancellationToken _) => m);
        _mockMessageService.Setup(m=>m.GetBySessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<Message>());
        _mockLLMService.Setup(l=>l.SendMessageStreamAsync(It.IsAny<IList<Message>>(), It.IsAny<CancellationToken>())).Returns(chunks.ToAsyncEnumerable());

        var collected = new List<ChatSessionEvent>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var readerTask = Task.Run(async () => {
            await foreach (var evt in _eventStream.Reader.ReadAllAsync(cts.Token))
            {
                collected.Add(evt);
                if (evt.Type == ChatSessionEventType.MessageComplete) break;
            }
        });

        await foreach (var _ in _chatSession.SendMessageStreamAsync("Hello")) { }
        await readerTask;

        collected.Should().Contain(e=>e.Type==ChatSessionEventType.MessageStart);
        collected.Should().Contain(e=>e.Type==ChatSessionEventType.ToolIterationStart);
        collected.Should().Contain(e=>e.Type==ChatSessionEventType.ToolIterationEnd);
        collected.Count(e=>e.Type==ChatSessionEventType.StreamDelta).Should().BeGreaterOrEqualTo(2);
        collected.Last().Type.Should().Be(ChatSessionEventType.MessageComplete);
    }

    private static async Task<List<ChatSessionEvent>> CollectEventsAsync(ChannelReader<ChatSessionEvent> reader, int count, TimeSpan timeout)
    {
        var list = new List<ChatSessionEvent>();
        var cts = new CancellationTokenSource(timeout);
        try
        {
            while (list.Count < count && await reader.WaitToReadAsync(cts.Token))
            {
                while (reader.TryRead(out var evt))
                {
                    list.Add(evt);
                    if (list.Count >= count) break;
                }
            }
        }
        catch (OperationCanceledException) { }
        return list;
    }

    private async Task SetupActiveSessionAsync()
    {
        var session = Session.Create("Test Session");
        _mockSessionService
            .Setup(s => s.CreateAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
            
        await _chatSession.StartNewSessionAsync("Test Session");
    }

    public void Dispose()
    {
        // Clean up any resources if needed
    }
}

// Extension method to help with async enumerable testing
public static class AsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            await Task.Yield(); // Make it actually async
            yield return item;
        }
    }
}
