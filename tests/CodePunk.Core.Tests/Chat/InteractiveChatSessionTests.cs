using CodePunk.Core.Abstractions;
using CodePunk.Core.Chat;
using CodePunk.Core.Models;
using CodePunk.Core.Services;
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
    private readonly InteractiveChatSession _chatSession;

    public InteractiveChatSessionTests()
    {
        _mockSessionService = new Mock<ISessionService>();
        _mockMessageService = new Mock<IMessageService>();
        _mockLLMService = new Mock<ILLMService>();
        
        _chatSession = new InteractiveChatSession(
            _mockSessionService.Object,
            _mockMessageService.Object,
            _mockLLMService.Object,
            NullLogger<InteractiveChatSession>.Instance);
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
        var aiResponse = Message.Create(
            _chatSession.CurrentSession!.Id,
            MessageRole.Assistant,
            [new TextPart(aiContent)],
            "gpt-4",
            "OpenAI");

        _mockMessageService
            .Setup(m => m.CreateAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Message m, CancellationToken _) => m);
            
        _mockMessageService
            .Setup(m => m.GetBySessionAsync(_chatSession.CurrentSession.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockMessages);
            
        _mockLLMService
            .Setup(l => l.SendMessageAsync(It.IsAny<IList<Message>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _chatSession.SendMessageAsync(userContent);

        // Assert
        result.Should().BeEquivalentTo(aiResponse);
        _chatSession.IsProcessing.Should().BeFalse();
        
        // Verify user message was saved
        _mockMessageService.Verify(m => m.CreateAsync(
            It.Is<Message>(msg => 
                msg.Role == MessageRole.User && 
                msg.Parts.OfType<TextPart>().First().Content == userContent),
            It.IsAny<CancellationToken>()), Times.Once);
            
        // Verify AI response was saved
        _mockMessageService.Verify(m => m.CreateAsync(aiResponse, It.IsAny<CancellationToken>()), Times.Once);
        
        // Verify LLM service was called
        _mockLLMService.Verify(l => l.SendMessageAsync(It.IsAny<IList<Message>>(), It.IsAny<CancellationToken>()), Times.Once);
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
        
        _mockLLMService
            .Setup(l => l.SendMessageAsync(It.IsAny<IList<Message>>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                processingStates.Add(_chatSession.IsProcessing);
                await Task.Delay(10); // Simulate processing time
                return Message.Create(_chatSession.CurrentSession!.Id, MessageRole.Assistant, [new TextPart("Response")]);
            });

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
