using CodePunk.Core.Abstractions;
using CodePunk.Core.Services;
using CodePunk.Data;
using CodePunk.Data.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodePunk.Core.Tests.Services;

public class SessionServiceTests
{
    private readonly ISessionService _sessionService;
    private readonly CodePunkDbContext _context;

    public SessionServiceTests()
    {
        var options = new DbContextOptionsBuilder<CodePunkDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
            
        _context = new CodePunkDbContext(options);
        var repository = new SessionRepository(_context);
        _sessionService = new SessionService(repository, NullLogger<SessionService>.Instance);
    }

    [Fact]
    public async Task CreateAsync_ShouldCreateSession_WithValidTitle()
    {
        // Arrange
        const string title = "Test Session";

        // Act
        var session = await _sessionService.CreateAsync(title);

        // Assert
        session.Should().NotBeNull();
        session.Title.Should().Be(title);
        session.Id.Should().NotBeEmpty();
        session.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CreateAsync_ShouldThrowException_WithEmptyTitle()
    {
        // Act & Assert
        await _sessionService.Invoking(s => s.CreateAsync(""))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*title*");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnSession_WhenExists()
    {
        // Arrange
        var session = await _sessionService.CreateAsync("Test Session");

        // Act
        var retrieved = await _sessionService.GetByIdAsync(session.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved.Should().BeEquivalentTo(session);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNull_WhenNotExists()
    {
        // Act
        var result = await _sessionService.GetByIdAsync("nonexistent-id");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRecentAsync_ShouldReturnOrderedSessions()
    {
        // Arrange
        var session1 = await _sessionService.CreateAsync("First Session");
        await Task.Delay(10); // Ensure different timestamps
        var session2 = await _sessionService.CreateAsync("Second Session");

        // Act
        var sessions = await _sessionService.GetRecentAsync(10);

        // Assert
        sessions.Should().HaveCount(2);
        sessions.First().Id.Should().Be(session2.Id); // Most recent first
        sessions.Last().Id.Should().Be(session1.Id);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
