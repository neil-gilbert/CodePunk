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
        
        // Create updated session with same ID but different properties
        var updatedSession = originalSession with 
        { 
            Title = "Updated Title",
            MessageCount = 5,
            Cost = 0.05m
        };
        
        // Act
        await SessionService.UpdateAsync(updatedSession);
        
        // Assert
        var retrievedSession = await SessionService.GetByIdAsync(originalSession.Id);
        retrievedSession.Should().NotBeNull();
        retrievedSession!.Title.Should().Be("Updated Title");
        retrievedSession.MessageCount.Should().Be(5);
        retrievedSession.Cost.Should().Be(0.05m);
        retrievedSession.CreatedAt.Should().Be(originalSession.CreatedAt); // Should not change
    }
}
