using System.Runtime.CompilerServices;
using System.Text;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Caching;
using CodePunk.Core.Models;
using CodePunk.Core.Services;
using CodePunk.Core.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CodePunk.Core.Tests.Caching;

/// <summary>
/// Validates integration between LLM service and prompt cache.
/// </summary>
public class LLMServicePromptCacheTests
{
    [Fact]
    public async Task SendAsync_UsesCache_OnRepeatedRequests()
    {
        var provider = new TestProvider();
        var factory = new TestProviderFactory(provider);
        var promptProvider = new TestPromptProvider();
        var toolService = new TestToolService();
        var cacheOptions = Options.Create(new PromptCacheOptions { Enabled = true, DefaultTtl = TimeSpan.FromMinutes(30) });
        var timeProvider = new TestTimeProvider();
        var cache = new PromptCache(new InMemoryPromptCacheStore(timeProvider), new DefaultPromptCacheKeyBuilder(), cacheOptions, timeProvider);
        var service = new LLMService(factory, promptProvider, toolService, cache, cacheOptions);
        var request = BuildRequest();

        var first = await service.SendAsync(request, CancellationToken.None);
        var second = await service.SendAsync(request, CancellationToken.None);

        first.Content.Should().Be(second.Content);
        provider.SendCount.Should().Be(1);
    }

    [Fact]
    public async Task SendMessageStreamAsync_UsesCache_OnRepeatedRequests()
    {
        var provider = new TestProvider();
        var factory = new TestProviderFactory(provider);
        var promptProvider = new TestPromptProvider();
        var toolService = new TestToolService();
        var cacheOptions = Options.Create(new PromptCacheOptions { Enabled = true, DefaultTtl = TimeSpan.FromMinutes(30) });
        var timeProvider = new TestTimeProvider();
        var cache = new PromptCache(new InMemoryPromptCacheStore(timeProvider), new DefaultPromptCacheKeyBuilder(), cacheOptions, timeProvider);
        var service = new LLMService(factory, promptProvider, toolService, cache, cacheOptions);
        var messages = new List<Message>
        {
            Message.Create("session", MessageRole.User, new[] { new TextPart("Hello there") })
        };

        var first = await CollectStreamAsync(service.SendMessageStreamAsync(messages, CancellationToken.None));
        var second = await CollectStreamAsync(service.SendMessageStreamAsync(messages, CancellationToken.None));

        first.Should().Be("hello world");
        second.Should().Be("hello world");
        provider.StreamCount.Should().Be(1);
    }

    private static LLMRequest BuildRequest()
    {
        return new LLMRequest
        {
            ModelId = "claude-3-opus",
            SystemPrompt = "system",
            Messages = new[]
            {
                Message.Create("session", MessageRole.User, new[] { new TextPart("Hello there") })
            }
        };
    }

    private sealed class TestProvider : ILLMProvider
    {
        public int SendCount { get; private set; }
        public int StreamCount { get; private set; }

        public string Name => "Anthropic";

        public IReadOnlyList<LLMModel> Models { get; } = new[] { new LLMModel { Id = "claude-3-opus", Name = "Claude 3 Opus" } };

        public Task<IReadOnlyList<LLMModel>> FetchModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Models);

        public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default)
        {
            SendCount++;
            return Task.FromResult(new LLMResponse { Content = $"fresh-{SendCount}" });
        }

        public async IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            StreamCount++;
            await Task.CompletedTask;
            yield return new LLMStreamChunk { Content = "hello ", IsComplete = false };
            yield return new LLMStreamChunk
            {
                Content = "world",
                IsComplete = true,
                Usage = new LLMUsage { InputTokens = 4, OutputTokens = 6, EstimatedCost = 0.01m },
                FinishReason = LLMFinishReason.Stop
            };
        }
    }

    private sealed class TestProviderFactory : ILLMProviderFactory
    {
        private readonly ILLMProvider _provider;

        public TestProviderFactory(ILLMProvider provider)
        {
            _provider = provider;
        }

        public ILLMProvider GetProvider(string? providerName = null) => _provider;

        public IEnumerable<string> GetAvailableProviders() => new[] { _provider.Name };
    }

    private sealed class TestPromptProvider : IPromptProvider
    {
        public IEnumerable<PromptType> GetAvailablePromptTypes(string providerName) => new[] { PromptType.Coder };

        public string GetSystemPrompt(string providerName, PromptType promptType = PromptType.Coder) => "system";
    }

    private sealed class TestToolService : IToolService
    {
        public IReadOnlyList<ITool> GetTools() => Array.Empty<ITool>();

        public ITool? GetTool(string name) => null;

        public Task<ToolResult> ExecuteAsync(string toolName, System.Text.Json.JsonElement arguments, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ToolResult { Content = string.Empty });

        public IReadOnlyList<LLMTool> GetLLMTools() => Array.Empty<LLMTool>();
    }

    private static async Task<string> CollectStreamAsync(IAsyncEnumerable<LLMStreamChunk> stream)
    {
        var builder = new StringBuilder();
        await foreach (var chunk in stream)
        {
            if (!string.IsNullOrEmpty(chunk.Content))
            {
                builder.Append(chunk.Content);
            }
        }

        return builder.ToString();
    }
}
