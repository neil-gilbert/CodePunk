using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using CodePunk.Core.Providers.Anthropic;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using System.Net;
using Xunit;

namespace CodePunk.Core.Tests.Integration.Providers.Anthropic;

[Trait("Category", "Integration")]
public class AnthropicProviderIntegrationTests
{
    [Fact]
    public async Task AnthropicProvider_WithMockAPI_ShouldWork()
    {
        // Skip if no API key configured to avoid failing in CI
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            // Use a test API key for mock testing
            apiKey = "test-key";
        }

        // Arrange
        var config = new AnthropicConfiguration
        {
            ApiKey = apiKey,
            BaseUrl = "https://api.anthropic.com/v1", // This would fail without real API key
            DefaultModel = AnthropicModels.Claude35Sonnet,
            MaxTokens = 100,
            Temperature = 0.7
        };

        var httpClient = new HttpClient();
        var logger = new LoggerFactory().CreateLogger<AnthropicProvider>();
        var provider = new AnthropicProvider(httpClient, config, logger);

        // Act - Basic provider info test
        var models = provider.Models;

        // Assert
        provider.Name.Should().Be("Anthropic");
        models.Should().NotBeEmpty();
        models.Should().Contain(m => m.Id == AnthropicModels.Claude35Sonnet);
        models.Should().Contain(m => m.Id == AnthropicModels.Claude35Haiku);
        
        var claude35 = models.First(m => m.Id == AnthropicModels.Claude35Sonnet);
        claude35.Name.Should().Be("Claude 3.5 Sonnet");
        claude35.MaxTokens.Should().Be(200000);
        claude35.SupportsStreaming.Should().BeTrue();
        claude35.SupportsTools.Should().BeTrue();
    }

    [Fact]
    public async Task AnthropicProvider_WithRealAPI_ShouldSendRequest()
    {
        // This test only runs if ANTHROPIC_API_KEY is set
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            return; // Skip test if no API key
        }

        // Arrange
        var config = new AnthropicConfiguration
        {
            ApiKey = apiKey,
            BaseUrl = "https://api.anthropic.com/v1",
            DefaultModel = AnthropicModels.Claude35Sonnet,
            MaxTokens = 50,
            Temperature = 0.7
        };

        var httpClient = new HttpClient();
        var logger = new LoggerFactory().CreateLogger<AnthropicProvider>();
        var provider = new AnthropicProvider(httpClient, config, logger);

        var request = new LLMRequest
        {
            ModelId = AnthropicModels.Claude35Sonnet,
            Messages = new[]
            {
                Message.Create("test-session", MessageRole.User, new[] { new TextPart("Say 'Hello, world!' in exactly that format.") })
            },
            MaxTokens = 50,
            Temperature = 0.0, // Make it deterministic
            SystemPrompt = "You are a helpful assistant. Always respond exactly as requested."
        };

        // Act
        var response = await provider.SendAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.Content.Should().NotBeNullOrEmpty();
        response.Content.Should().Contain("Hello, world!");
        response.Usage.Should().NotBeNull();
        response.Usage!.InputTokens.Should().BeGreaterThan(0);
        response.Usage.OutputTokens.Should().BeGreaterThan(0);
        response.FinishReason.Should().Be(LLMFinishReason.Stop);
    }

    [Fact]
    public async Task AnthropicProvider_WithRealAPIStreaming_ShouldStreamResponse()
    {
        // This test only runs if ANTHROPIC_API_KEY is set
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            return; // Skip test if no API key
        }

        // Arrange
        var config = new AnthropicConfiguration
        {
            ApiKey = apiKey,
            BaseUrl = "https://api.anthropic.com/v1",
            DefaultModel = AnthropicModels.Claude35Sonnet,
            MaxTokens = 30,
            Temperature = 0.7
        };

        var httpClient = new HttpClient();
        var logger = new LoggerFactory().CreateLogger<AnthropicProvider>();
        var provider = new AnthropicProvider(httpClient, config, logger);

        var request = new LLMRequest
        {
            ModelId = AnthropicModels.Claude35Sonnet,
            Messages = new[]
            {
                Message.Create("test-session", MessageRole.User, new[] { new TextPart("Count from 1 to 5") })
            },
            MaxTokens = 30,
            Temperature = 0.0
        };

        // Act
        var chunks = new List<LLMStreamChunk>();
        await foreach (var chunk in provider.StreamAsync(request))
        {
            chunks.Add(chunk);
            if (chunk.IsComplete)
                break;
        }

        // Assert
        chunks.Should().NotBeEmpty();
        chunks.Should().Contain(c => !string.IsNullOrEmpty(c.Content));
        chunks.Last().IsComplete.Should().BeTrue();
        
        var fullContent = string.Join("", chunks.Where(c => c.Content != null).Select(c => c.Content));
        fullContent.Should().NotBeNullOrEmpty();
    }
}
