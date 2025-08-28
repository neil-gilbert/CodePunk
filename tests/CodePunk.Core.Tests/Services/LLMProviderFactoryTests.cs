using CodePunk.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CodePunk.Core.Tests.Services;

public class LLMProviderFactoryTests
{
    [Fact]
    public void GetProvider_WithUnsupportedProvider_ShouldThrowNotSupportedException()
    {
        // Arrange
        var configuration = CreateConfiguration("openai");
        var serviceProvider = CreateEmptyServiceProvider();
        var factory = new LLMProviderFactory(serviceProvider, configuration);

        // Act & Assert
        factory.Invoking(f => f.GetProvider("unsupported"))
            .Should().Throw<NotSupportedException>()
            .WithMessage("*unsupported*not supported*");
    }

    [Fact]
    public void GetProvider_WhenProviderNotConfigured_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var configuration = CreateConfiguration("openai");
        var serviceProvider = CreateEmptyServiceProvider();
        var factory = new LLMProviderFactory(serviceProvider, configuration);

        // Act & Assert
        factory.Invoking(f => f.GetProvider("openai"))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*OpenAI provider is not configured*");
    }

    [Fact]
    public void GetAvailableProviders_WithNoProviders_ShouldReturnEmpty()
    {
        // Arrange
        var configuration = CreateConfiguration("openai");
        var serviceProvider = CreateEmptyServiceProvider();
        var factory = new LLMProviderFactory(serviceProvider, configuration);

        // Act
        var providers = factory.GetAvailableProviders().ToList();

        // Assert
        providers.Should().BeEmpty();
    }

    [Theory]
    [InlineData("anthropic")]
    [InlineData("ANTHROPIC")]
    [InlineData("Anthropic")]
    public void GetProvider_WithDifferentCasing_ShouldBeCaseInsensitive(string providerName)
    {
        // Arrange
        var configuration = CreateConfiguration("anthropic");
        var serviceProvider = CreateEmptyServiceProvider();
        var factory = new LLMProviderFactory(serviceProvider, configuration);

        // Act & Assert
        // All should throw the same error since no providers are configured
        factory.Invoking(f => f.GetProvider(providerName))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*Anthropic provider is not configured*");
    }

    private IConfiguration CreateConfiguration(string defaultProvider)
    {
        var configData = new Dictionary<string, string>
        {
            ["AI:DefaultProvider"] = defaultProvider
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(configData!)
            .Build();
    }

    private IServiceProvider CreateEmptyServiceProvider()
    {
        var serviceCollection = new ServiceCollection();
        return serviceCollection.BuildServiceProvider();
    }
}
