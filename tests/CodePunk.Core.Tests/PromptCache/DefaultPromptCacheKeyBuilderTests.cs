using System.Text.Json;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Caching;
using CodePunk.Core.Models;
using FluentAssertions;
using Xunit;

namespace CodePunk.Core.Tests.Caching;

/// <summary>
/// Verifies deterministic behavior of the default prompt cache key builder.
/// </summary>
public class DefaultPromptCacheKeyBuilderTests
{
    [Fact]
    public void Build_ReturnsSameKey_ForEquivalentContexts()
    {
        var builder = new DefaultPromptCacheKeyBuilder();
        var context = CreateContext();

        var first = builder.Build(context);
        var second = builder.Build(context);

        first.Value.Should().Be(second.Value);
    }

    [Fact]
    public void Build_ProducesDifferentKeys_WhenMessageContentDiffers()
    {
        var builder = new DefaultPromptCacheKeyBuilder();
        var context = CreateContext();
        var altered = context with
        {
            Request = context.Request with
            {
                Messages = new[]
                {
                    Message.Create("session", MessageRole.User, new[] { new TextPart("hello world") })
                }
            }
        };

        var baseline = builder.Build(context);
        var modified = builder.Build(altered);

        baseline.Value.Should().NotBe(modified.Value);
    }

    [Fact]
    public void Build_ProducesDifferentKeys_WhenToolingDiffers()
    {
        var builder = new DefaultPromptCacheKeyBuilder();
        var context = CreateContext();
        var altered = context with
        {
            Request = context.Request with
            {
                Tools = new[]
                {
                    new LLMTool
                    {
                        Name = "search",
                        Description = "search",
                        Parameters = JsonDocument.Parse("""{"type":"object"}""").RootElement
                    }
                }
            }
        };

        var baseline = builder.Build(context);
        var modified = builder.Build(altered);

        baseline.Value.Should().NotBe(modified.Value);
    }

    private static PromptCacheContext CreateContext()
    {
        var request = new LLMRequest
        {
            ModelId = "claude-3-opus",
            SystemPrompt = "system",
            Temperature = 0.2,
            TopP = 0.9,
            MaxTokens = 8000,
            Messages = new[]
            {
                Message.Create("session", MessageRole.User, new[] { new TextPart("hello") })
            }
        };

        return new PromptCacheContext("anthropic", request);
    }
}
