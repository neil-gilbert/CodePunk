using CodePunk.Core.Models;
using FluentAssertions;
using Xunit;

namespace CodePunk.Core.Tests.Models;

public class SessionTests
{
    [Fact]
    public void Create_ShouldGenerateValidSession()
    {
        // Arrange
        const string title = "Test Session";

        // Act
        var session = Session.Create(title);

        // Assert
        session.Id.Should().NotBeEmpty();
        session.Title.Should().Be(title);
        session.ParentSessionId.Should().BeNull();
        session.MessageCount.Should().Be(0);
        session.PromptTokens.Should().Be(0);
        session.CompletionTokens.Should().Be(0);
        session.Cost.Should().Be(0);
        session.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        session.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        session.SummaryMessageId.Should().BeNull();
    }

    [Fact]
    public void Create_ShouldSetParentSessionId_WhenProvided()
    {
        // Arrange
        const string title = "Child Session";
        const string parentId = "parent-session-id";

        // Act
        var session = Session.Create(title, parentId);

        // Assert
        session.ParentSessionId.Should().Be(parentId);
    }
}
