using CodePunk.Core.Abstractions;
using CodePunk.Core.Chat;
using CodePunk.Core.Models;
using CodePunk.Core.Services;
using CodePunk.Data;
using CodePunk.Data.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CodePunk.Integration.Tests.Chat;

public class InteractiveChatIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly CodePunkDbContext _context;
    private readonly Mock<ILLMService> _mockLLMService;

    public InteractiveChatIntegrationTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<CodePunkDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
            
        _context = new CodePunkDbContext(options);
        
        // Setup mock LLM service
        _mockLLMService = new Mock<ILLMService>();
        
        // Configure services
        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton<ISessionRepository, SessionRepository>();
        services.AddSingleton<IMessageRepository, MessageRepository>();
        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<IMessageService, MessageService>();
        services.AddSingleton<IToolService, ToolService>();
        services.AddSingleton(_mockLLMService.Object);
        services.AddSingleton<InteractiveChatSession>();
        services.AddLogging(builder => builder.AddConsole());
        
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task FullChatFlow_ShouldWorkEndToEnd()
    {
        // Arrange
        var chatSession = _serviceProvider.GetRequiredService<InteractiveChatSession>();
        var sessionService = _serviceProvider.GetRequiredService<ISessionService>();
        var messageService = _serviceProvider.GetRequiredService<IMessageService>();
        
        const string aiResponseContent = "Hello! I'm CodePunk, your AI assistant.";
        var aiResponse = Message.Create(
            "session-id", // Will be replaced with actual session ID
            MessageRole.Assistant,
            [new TextPart(aiResponseContent)],
            "gpt-4",
            "OpenAI");

        _mockLLMService
            .Setup(l => l.SendMessageStreamAsync(It.IsAny<IList<Message>>(), It.IsAny<CancellationToken>()))
            .Returns((IList<Message> messages, CancellationToken ct) =>
            {
                // Create stream response
                var streamChunks = new List<LLMStreamChunk>
                {
                    new() { Content = aiResponseContent, IsComplete = true }
                };
                return streamChunks.ToAsyncEnumerable();
            });

        // Act & Assert

        // 1. Start a new session
        var session = await chatSession.StartNewSessionAsync("Integration Test Chat");
        session.Should().NotBeNull();
        session.Title.Should().Be("Integration Test Chat");
        chatSession.IsActive.Should().BeTrue();

        // 2. Verify session was persisted
        var persistedSession = await sessionService.GetByIdAsync(session.Id);
        persistedSession.Should().NotBeNull();
        persistedSession!.Title.Should().Be("Integration Test Chat");

        // 3. Send a message and get response
        const string userMessage = "Hello, AI!";
        var response = await chatSession.SendMessageAsync(userMessage);
        
        response.Should().NotBeNull();
        response.Role.Should().Be(MessageRole.Assistant);
        response.Parts.OfType<TextPart>().First().Content.Should().Be(aiResponseContent);

        // 4. Verify messages were persisted
        var messages = await messageService.GetBySessionAsync(session.Id);
        messages.Should().HaveCount(2);
        
        var userMsg = messages.First(m => m.Role == MessageRole.User);
        var aiMsg = messages.First(m => m.Role == MessageRole.Assistant);
        
        userMsg.Parts.OfType<TextPart>().First().Content.Should().Be(userMessage);
        aiMsg.Parts.OfType<TextPart>().First().Content.Should().Be(aiResponseContent);

        // 5. Get conversation history
        var history = await chatSession.GetConversationHistoryAsync();
        history.Should().HaveCount(2);
        history.Should().BeEquivalentTo(messages);

        // 6. Load session in new chat instance
        var newChatSession = _serviceProvider.GetRequiredService<InteractiveChatSession>();
        var loaded = await newChatSession.LoadSessionAsync(session.Id);
        loaded.Should().BeTrue();
        newChatSession.CurrentSession.Should().NotBeNull();
        newChatSession.CurrentSession!.Id.Should().Be(session.Id);

        // 7. Continue conversation in loaded session
        const string followUpMessage = "Tell me more!";
        const string followUpResponse = "Sure! I can help with that.";
        
        _mockLLMService
            .Setup(l => l.SendMessageStreamAsync(It.IsAny<IList<Message>>(), It.IsAny<CancellationToken>()))
            .Returns((IList<Message> msgs, CancellationToken ct) =>
            {
                var streamChunks = new List<LLMStreamChunk>
                {
                    new() { Content = followUpResponse, IsComplete = true }
                };
                return streamChunks.ToAsyncEnumerable();
            });

        var followUpResponseMsg = await newChatSession.SendMessageAsync(followUpMessage);
        followUpResponseMsg.Parts.OfType<TextPart>().First().Content.Should().Be(followUpResponse);

        // 8. Verify complete conversation history
        var finalHistory = await newChatSession.GetConversationHistoryAsync();
        finalHistory.Should().HaveCount(4);
        
        var allMessages = finalHistory.ToList();
        allMessages[0].Parts.OfType<TextPart>().First().Content.Should().Be(userMessage);
        allMessages[1].Parts.OfType<TextPart>().First().Content.Should().Be(aiResponseContent);
        allMessages[2].Parts.OfType<TextPart>().First().Content.Should().Be(followUpMessage);
        allMessages[3].Parts.OfType<TextPart>().First().Content.Should().Be(followUpResponse);
    }

    [Fact]
    public async Task StreamingChatFlow_ShouldWorkEndToEnd()
    {
        // Arrange
        var chatSession = _serviceProvider.GetRequiredService<InteractiveChatSession>();
        var messageService = _serviceProvider.GetRequiredService<IMessageService>();
        
        var streamChunks = new List<LLMStreamChunk>
        {
            new() { Content = "Hello", IsComplete = false },
            new() { Content = " there", IsComplete = false },
            new() { Content = "! How", IsComplete = false },
            new() { Content = " can I help?", IsComplete = true }
        };

        _mockLLMService
            .Setup(l => l.SendMessageStreamAsync(It.IsAny<List<Message>>(), It.IsAny<CancellationToken>()))
            .Returns(streamChunks.ToAsyncEnumerable());

        // Act
        var session = await chatSession.StartNewSessionAsync("Streaming Test");
        
        var receivedChunks = new List<ChatStreamChunk>();
        await foreach (var chunk in chatSession.SendMessageStreamAsync("Hello AI"))
        {
            receivedChunks.Add(chunk);
        }

        // Assert
        receivedChunks.Should().HaveCount(4);
        receivedChunks.All(c => c.Model == "claude-3-5-sonnet").Should().BeTrue();
        receivedChunks.All(c => c.Provider == "Anthropic").Should().BeTrue();
        receivedChunks.Last().IsComplete.Should().BeTrue();

        // Verify complete message was saved
        var messages = await messageService.GetBySessionAsync(session.Id);
        messages.Should().HaveCount(2);
        
        var aiMessage = messages.First(m => m.Role == MessageRole.Assistant);
        aiMessage.Parts.OfType<TextPart>().First().Content.Should().Be("Hello there! How can I help?");
    }

    [Fact]
    public async Task MultipleSessionsFlow_ShouldWorkCorrectly()
    {
        // Arrange
        var sessionService = _serviceProvider.GetRequiredService<ISessionService>();
        var chatSession = _serviceProvider.GetRequiredService<InteractiveChatSession>();

        _mockLLMService
            .Setup(l => l.SendMessageStreamAsync(It.IsAny<IList<Message>>(), It.IsAny<CancellationToken>()))
            .Returns((IList<Message> messages, CancellationToken ct) =>
            {
                var streamChunks = new List<LLMStreamChunk>
                {
                    new() { Content = "Response", IsComplete = true }
                };
                return streamChunks.ToAsyncEnumerable();
            });

        // Act & Assert

        // Create multiple sessions
        var session1 = await chatSession.StartNewSessionAsync("Session 1");
        await chatSession.SendMessageAsync("Message 1");
        
        var session2 = await chatSession.StartNewSessionAsync("Session 2");
        await chatSession.SendMessageAsync("Message 2");
        
        var session3 = await chatSession.StartNewSessionAsync("Session 3");
        await chatSession.SendMessageAsync("Message 3");

        // Verify sessions exist
        var allSessions = await sessionService.GetRecentAsync(100);
        allSessions.Should().HaveCountGreaterOrEqualTo(3);
        
        var createdSessions = allSessions.Where(s => 
            s.Title == "Session 1" || 
            s.Title == "Session 2" || 
            s.Title == "Session 3").ToList();
        
        createdSessions.Should().HaveCount(3);

        // Switch between sessions
        await chatSession.LoadSessionAsync(session1.Id);
        chatSession.CurrentSession!.Id.Should().Be(session1.Id);
        
        await chatSession.LoadSessionAsync(session2.Id);
        chatSession.CurrentSession!.Id.Should().Be(session2.Id);

        // Each session should have its own message history
        var session1History = await chatSession.LoadSessionAsync(session1.Id) 
            ? await chatSession.GetConversationHistoryAsync() 
            : new List<Message>();
            
        await chatSession.LoadSessionAsync(session2.Id);
        var session2History = await chatSession.GetConversationHistoryAsync();

        session1History.Should().HaveCount(2);
        session2History.Should().HaveCount(2);
        session1History.Should().NotBeEquivalentTo(session2History);
    }

    [Fact]
    public async Task ErrorHandling_ShouldWorkCorrectly()
    {
        // Arrange
        var chatSession = _serviceProvider.GetRequiredService<InteractiveChatSession>();

        // Act & Assert

        // 1. Should throw when no active session
        await chatSession.Invoking(cs => cs.SendMessageAsync("Hello"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("No active session. Start a new session first.");

        // 2. Should return empty history when no active session
        var history = await chatSession.GetConversationHistoryAsync();
        history.Should().BeEmpty();

        // 3. Should return false when loading non-existent session
        var loaded = await chatSession.LoadSessionAsync("non-existent-id");
        loaded.Should().BeFalse();
        chatSession.IsActive.Should().BeFalse();

        // 4. Should handle LLM service errors gracefully
        await chatSession.StartNewSessionAsync("Error Test");
        
        _mockLLMService
            .Setup(l => l.SendMessageStreamAsync(It.IsAny<IList<Message>>(), It.IsAny<CancellationToken>()))
            .Returns(() => throw new InvalidOperationException("LLM service error"));

        await chatSession.Invoking(cs => cs.SendMessageAsync("Hello"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("LLM service error");

        // Processing state should be reset even after error
        chatSession.IsProcessing.Should().BeFalse();
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
        _context?.Dispose();
    }
}

// Extension method for async enumerable testing
public static class TestAsyncEnumerableExtensions
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
