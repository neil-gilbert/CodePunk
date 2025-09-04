using CodePunk.Core.Abstractions;
using CodePunk.Core.Chat;
using CodePunk.Core.Models;
using CodePunk.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace CodePunk.Integration.Tests.Developer;

/// <summary>
/// Developer tests that demonstrate and validate the complete Phase 3 implementation.
/// These tests serve as both validation and documentation of the interactive chat features.
/// </summary>
public class Phase3DeveloperTests
{
    private readonly ITestOutputHelper _output;

    public Phase3DeveloperTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Phase3A_SessionManagement_ShouldWork()
    {
        _output.WriteLine("=== Phase 3A: Session Management Test ===");

        var chatSession = CreateMockChatSession();

        // Test error scenarios
        _output.WriteLine("1. Testing no active session error...");
        await chatSession.Invoking(cs => cs.SendMessageAsync("Hello"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("No active session. Start a new session first.");
        _output.WriteLine("   ✓ Correctly throws when no active session");

        _output.WriteLine("2. Testing empty conversation history...");
        var emptyHistory = await chatSession.GetConversationHistoryAsync();
        emptyHistory.Should().BeEmpty();
        _output.WriteLine("   ✓ Returns empty history when no active session");

        _output.WriteLine("3. Testing session creation and clearing...");
        await chatSession.StartNewSessionAsync("Test Session");
        chatSession.IsActive.Should().BeTrue();
        
        chatSession.ClearSession();
        chatSession.IsActive.Should().BeFalse();
        _output.WriteLine("   ✓ Session creation and clearing works");

        _output.WriteLine("=== Session Management Test Complete ===\n");
    }

    [Fact]
    public void Phase3A_Architecture_ShouldBeValid()
    {
        _output.WriteLine("=== Phase 3A: Architecture Validation ===");

        // Validate that InteractiveChatSession uses proper dependency injection
        var chatSessionType = typeof(InteractiveChatSession);
        var constructor = chatSessionType.GetConstructors().First();
        var dependencies = constructor.GetParameters().Select(p => p.ParameterType);
        
        var allDependenciesAreInterfaces = dependencies.All(dep => 
            dep.IsInterface || dep.Name.Contains("ILogger"));
            
        allDependenciesAreInterfaces.Should().BeTrue("All dependencies should be interfaces for testability");
        _output.WriteLine("   ✓ Dependencies properly abstracted through interfaces");

        _output.WriteLine("=== Architecture Validation Complete ===\n");
    }

    private InteractiveChatSession CreateMockChatSession()
    {
        var mockSessionService = new Mock<ISessionService>();
        var mockMessageService = new Mock<IMessageService>();
        var mockLLMService = new Mock<ILLMService>();

        // Setup session creation
        mockSessionService
            .Setup(s => s.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string title, string description, CancellationToken ct) => Session.Create(title));

        // Setup message operations
        mockMessageService
            .Setup(m => m.CreateAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Message message, CancellationToken ct) => message);

        var messages = new List<Message>();
        mockMessageService
            .Setup(m => m.GetBySessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => messages.ToList());

        var mockToolService = new Mock<IToolService>();

        return new InteractiveChatSession(
            mockSessionService.Object,
            mockMessageService.Object,
            mockLLMService.Object,
            mockToolService.Object,
            NullLogger<InteractiveChatSession>.Instance);
    }
}
