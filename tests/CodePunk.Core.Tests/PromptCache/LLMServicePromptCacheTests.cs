using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Caching;
using CodePunk.Core.Models;
using CodePunk.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CodePunk.Core.Tests.Caching;

/// <summary>
/// Exercises prompt cache behaviour within the LLM service when providers support remote caching.
/// </summary>
public class LLMServicePromptCacheTests
{
    [Fact]
    public async Task FirstCall_RequestsEphemeralCache_WhenEnabled()
    {
        var provider = new RecordingProvider();
        provider.CacheInfos.Enqueue(new LLMPromptCacheInfo
        {
            CacheId = "cache:test",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        });

        var service = CreateService(provider);
        var request = BuildRequest();

        await service.SendAsync(request, CancellationToken.None);

        provider.SentRequests.Should().HaveCount(1);
        var first = provider.SentRequests[0];
        first.UseEphemeralCache.Should().BeTrue();
        first.SystemPrompt.Should().Be("system");
        first.SystemPromptCacheId.Should().BeNull();
    }

    [Fact]
    public async Task SubsequentCall_ReusesCacheReference()
    {
        var provider = new RecordingProvider();
        provider.CacheInfos.Enqueue(new LLMPromptCacheInfo
        {
            CacheId = "cache:abc123",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(20)
        });
        provider.CacheInfos.Enqueue(null); // Provider typically omits cache metadata on reuse

        var service = CreateService(provider);
        var request = BuildRequest();

        await service.SendAsync(request, CancellationToken.None);
        await service.SendAsync(request, CancellationToken.None);

        provider.SentRequests.Should().HaveCount(2);
        var second = provider.SentRequests[1];
        second.UseEphemeralCache.Should().BeFalse();
        second.SystemPrompt.Should().BeNull();
        second.SystemPromptCacheId.Should().Be("cache:abc123");
    }

    [Fact]
    public async Task ProviderWithoutCache_DisablesFutureAttempts()
    {
        var provider = new RecordingProvider();
        provider.CacheInfos.Enqueue(null); // Provider does not return cache metadata
        provider.CacheInfos.Enqueue(null);

        var service = CreateService(provider);
        var request = BuildRequest();

        await service.SendAsync(request, CancellationToken.None);
        await service.SendAsync(request, CancellationToken.None);

        provider.SentRequests.Should().HaveCount(2);
        var first = provider.SentRequests[0];
        var second = provider.SentRequests[1];

        first.UseEphemeralCache.Should().BeTrue();
        second.UseEphemeralCache.Should().BeFalse();
        second.SystemPrompt.Should().Be("system");
        second.SystemPromptCacheId.Should().BeNull();
    }

    private static LLMService CreateService(RecordingProvider provider)
    {
        var factory = new TestProviderFactory(provider);
        var promptProvider = new TestPromptProvider();
        var toolService = new TestToolService();
        var cacheOptions = Options.Create(new PromptCacheOptions { Enabled = true, DefaultTtl = TimeSpan.FromMinutes(30) });
        var timeProvider = TimeProvider.System;
        var cache = new PromptCache(new InMemoryPromptCacheStore(timeProvider), new DefaultPromptCacheKeyBuilder(), cacheOptions, timeProvider);
        return new LLMService(factory, promptProvider, toolService, cache, cacheOptions);
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

    private sealed class RecordingProvider : ILLMProvider
    {
        public Queue<LLMPromptCacheInfo?> CacheInfos { get; } = new();
        public List<LLMRequest> SentRequests { get; } = new();

        public string Name => "Anthropic";

        public IReadOnlyList<LLMModel> Models { get; } = new[]
        {
            new LLMModel { Id = "claude-3-opus", Name = "Claude 3 Opus" }
        };

        public Task<IReadOnlyList<LLMModel>> FetchModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Models);

        public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default)
        {
            SentRequests.Add(request);
            var cacheInfo = CacheInfos.Count > 0 ? CacheInfos.Dequeue() : null;
            return Task.FromResult(new LLMResponse
            {
                Content = "ok",
                PromptCache = cacheInfo
            });
        }

        public async IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            SentRequests.Add(request);
            var cacheInfo = CacheInfos.Count > 0 ? CacheInfos.Dequeue() : null;
            yield return new LLMStreamChunk { PromptCache = cacheInfo, IsComplete = true };
            await Task.CompletedTask;
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
}
