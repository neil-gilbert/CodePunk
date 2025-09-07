using CodePunk.Core.Models;
using FluentAssertions;
using Xunit;

namespace CodePunk.Integration.Tests.Developer;

/// <summary>
/// Verifies that SessionService.GetRecentAsync returns live message counts (not stale zeros).
/// </summary>
public class SessionMessageCountIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task GetRecent_ShouldIncludeLiveMessageCounts()
    {
        // Create a session
        var session = await SessionService.CreateAsync("Count Test");

        // Add a few messages via MessageService to simulate chat traffic
        for (int i = 0; i < 3; i++)
        {
            var user = Message.Create(session.Id, MessageRole.User, [ new TextPart($"Message {i}") ]);
            await MessageService.CreateAsync(user);
        }

        // Retrieve recent sessions
        var recent = await SessionService.GetRecentAsync(10);
        var target = recent.First(s => s.Id == session.Id);
        target.MessageCount.Should().Be(3);

        // Add another message
        var extra = Message.Create(session.Id, MessageRole.Assistant, [ new TextPart("Extra") ]);
        await MessageService.CreateAsync(extra);

        var recent2 = await SessionService.GetRecentAsync(10);
        var target2 = recent2.First(s => s.Id == session.Id);
        target2.MessageCount.Should().Be(4);
    }
}
