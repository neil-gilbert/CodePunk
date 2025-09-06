using CodePunk.Core.Services;
using FluentAssertions;
using Xunit;

namespace CodePunk.Core.Tests.Services;

public class PromptProviderTests
{
    private readonly IPromptProvider _promptProvider;

    public PromptProviderTests()
    {
        _promptProvider = new PromptProvider();
    }

    [Fact]
    public void GetSystemPrompt_WithOpenAI_ReturnsOpenAISpecificPrompt()
    {
        // Act
        var prompt = _promptProvider.GetSystemPrompt("OpenAI", PromptType.Coder);

        // Assert
        prompt.Should().NotBeNullOrEmpty();
    prompt.Should().Contain("CodePunk");
    // Base prompt content
    prompt.Should().Contain("collaborative software engineering assistant");
    }

    [Fact]
    public void GetSystemPrompt_WithAnthropic_ReturnsAnthropicSpecificPrompt()
    {
        // Act
        var prompt = _promptProvider.GetSystemPrompt("Anthropic", PromptType.Coder);

        // Assert
        prompt.Should().NotBeNullOrEmpty();
    prompt.Should().Contain("CodePunk");
    prompt.Should().Contain("collaborative software engineering assistant");
    }

    [Fact]
    public void GetSystemPrompt_WithGemini_ReturnsGeminiSpecificPrompt()
    {
        // Act
        var prompt = _promptProvider.GetSystemPrompt("Gemini", PromptType.Coder);

        // Assert
        prompt.Should().NotBeNullOrEmpty();
    prompt.Should().Contain("CodePunk");
    prompt.Should().Contain("collaborative software engineering assistant");
    }

    [Fact]
    public void GetSystemPrompt_WithUnknownProvider_FallsBackToOpenAI()
    {
        // Act
        var prompt = _promptProvider.GetSystemPrompt("UnknownProvider", PromptType.Coder);

        // Assert
        prompt.Should().NotBeNullOrEmpty();
        prompt.Should().Contain("CodePunk");
    prompt.Should().Contain("collaborative software engineering assistant");
    }

    [Fact]
    public void GetSystemPrompt_WithTitleType_ReturnsAppropriatePrompt()
    {
        // Act
        var prompt = _promptProvider.GetSystemPrompt("OpenAI", PromptType.Title);

        // Assert
        prompt.Should().NotBeNullOrEmpty();
        prompt.Should().Contain("title");
        prompt.Should().Contain("50 characters");
    }

    [Fact]
    public void GetAvailablePromptTypes_WithValidProvider_ReturnsPromptTypes()
    {
        // Act
        var promptTypes = _promptProvider.GetAvailablePromptTypes("OpenAI");

        // Assert
        promptTypes.Should().NotBeEmpty();
        promptTypes.Should().Contain(PromptType.Coder);
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("Anthropic")]
    [InlineData("Gemini")]
    public void GetSystemPrompt_WithDifferentProviders_ReturnsUniquePrompts(string providerName)
    {
        // Act
        var prompt = _promptProvider.GetSystemPrompt(providerName, PromptType.Coder);

        // Assert
        prompt.Should().NotBeNullOrEmpty();
        prompt.Should().Contain("CodePunk");
    prompt.Should().Contain("collaborative software engineering assistant");
        
        // Each provider should have distinct characteristics
        switch (providerName)
        {
            case "OpenAI":
                prompt.Should().Contain("agentic coding assistant"); // provider-specific content retained
                break;
            case "Anthropic":
                prompt.Should().Contain("concise");
                break;
            case "Gemini":
                prompt.Should().Contain("CLI agent");
                break;
        }
    }
}
