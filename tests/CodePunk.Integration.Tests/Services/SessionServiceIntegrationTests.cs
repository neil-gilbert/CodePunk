using FluentAssertions;
using CodePunk.Core.Models;

namespace CodePunk.Integration.Tests.Services;

/// <summary>
/// Integration tests for SessionService using Ports and Adapters pattern.
/// Tests the service interface (port) with real persistence (adapter).
/// </summary>
public class SessionServiceIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task CreateSession_ShouldPersistAndRetrieveThroughService()
    {
        // Arrange
        var title = "Integration Test Session";
        
        // Act - Test through service interface (port)
        var session = await SessionService.CreateAsync(title);
        
        // Assert - Test through service interface (port)
        var retrievedSession = await SessionService.GetByIdAsync(session.Id);
        
        retrievedSession.Should().NotBeNull();
        retrievedSession!.Title.Should().Be("Integration Test Session");
        retrievedSession.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task GetRecentSessions_ShouldReturnInCorrectOrder()
    {
        // Arrange
        var session1 = await SessionService.CreateAsync("First Session");
        await Task.Delay(10); // Ensure different timestamps
        var session2 = await SessionService.CreateAsync("Second Session");
        
        // Act
        var recentSessions = await SessionService.GetRecentAsync(10);
        
        // Assert
        recentSessions.Should().HaveCount(2);
        recentSessions[0].Title.Should().Be("Second Session"); // Most recent first
        recentSessions[1].Title.Should().Be("First Session");
        recentSessions[0].CreatedAt.Should().BeAfter(recentSessions[1].CreatedAt);
    }

    [Fact]
    public async Task DeleteSession_ShouldRemoveSessionThroughService()
    {
        // Arrange
        var session = await SessionService.CreateAsync("Session to Delete");
        
        // Verify it exists
        var existingSession = await SessionService.GetByIdAsync(session.Id);
        existingSession.Should().NotBeNull();
        
        // Act
        await SessionService.DeleteAsync(session.Id);
        
        // Assert
        var deletedSession = await SessionService.GetByIdAsync(session.Id);
        deletedSession.Should().BeNull();
    }

    [Fact]
    public async Task UpdateSession_ShouldModifySessionThroughService()
    {
        // Arrange
        var originalSession = await SessionService.CreateAsync("Original Title");
        
        // Create updated session with same ID but different properties.
        // NOTE: MessageCount is now derived from actual messages and recomputed live; we no longer set it directly.
        var updatedSession = originalSession with 
        {
            Title = "Updated Title",
            Cost = 0.05m
        };
        
        // Act
        await SessionService.UpdateAsync(updatedSession);
        
        // Assert
        var retrievedSession = await SessionService.GetByIdAsync(originalSession.Id);
        retrievedSession.Should().NotBeNull();
        retrievedSession!.Title.Should().Be("Updated Title");
        // MessageCount should remain zero because no messages were created
        retrievedSession.MessageCount.Should().Be(0);
        retrievedSession.Cost.Should().Be(0.05m);
        retrievedSession.CreatedAt.Should().Be(originalSession.CreatedAt); // Should not change
    }

    [Fact]
    public async Task SessionMessageCount_ShouldReflectActualMessages()
    {
        // Arrange - Create a session
        var session = await SessionService.CreateAsync("Test Session");
        
        // Verify initial count is zero
        var initialSession = await SessionService.GetByIdAsync(session.Id);
        initialSession!.MessageCount.Should().Be(0);
        
        // Act - Add some messages
        await MessageService.CreateAsync(new Message 
        { 
            Id = Guid.NewGuid().ToString(),
            SessionId = session.Id,
            Role = MessageRole.User,
            Parts = [new TextPart("Hello")],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        
        await MessageService.CreateAsync(new Message 
        { 
            Id = Guid.NewGuid().ToString(),
            SessionId = session.Id,
            Role = MessageRole.Assistant, 
            Parts = [new TextPart("Hi there!")],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        
        await MessageService.CreateAsync(new Message 
        { 
            Id = Guid.NewGuid().ToString(),
            SessionId = session.Id,
            Role = MessageRole.User,
            Parts = [new TextPart("How are you?")],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        
        // Assert - Check that message count is updated correctly
        var updatedSession = await SessionService.GetByIdAsync(session.Id);
        updatedSession!.MessageCount.Should().Be(3);
        
        // Also verify through GetRecentAsync
        var recentSessions = await SessionService.GetRecentAsync(10);
        var foundSession = recentSessions.FirstOrDefault(s => s.Id == session.Id);
        foundSession.Should().NotBeNull();
        foundSession!.MessageCount.Should().Be(3);
    }
}
