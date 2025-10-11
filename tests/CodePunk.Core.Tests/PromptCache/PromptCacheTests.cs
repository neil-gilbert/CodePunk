using System.Collections.Concurrent;
using System.Text.Json;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Caching;
using CodePunk.Core.Models;
using CodePunk.Core.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CodePunk.Core.Tests.Caching;

/// <summary>
/// Exercises the prompt cache orchestration logic.
/// </summary>
public class PromptCacheTests
{
    [Fact]
    public async Task TryGetAsync_ReturnsStoredResponse_WhenEntryPresent()
    {
        var context = BuildContext();
        var response = new LLMResponse { Content = "cached" };
        var store = new InMemoryPromptCacheStore();
        var options = Options.Create(new PromptCacheOptions { Enabled = true, DefaultTtl = TimeSpan.FromMinutes(5) });
        var cache = new PromptCache(store, new DefaultPromptCacheKeyBuilder(), options);

        await cache.StoreAsync(context, response, CancellationToken.None);
        var result = await cache.TryGetAsync(context, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Response.Content.Should().Be("cached");
    }

    [Fact]
    public async Task TryGetAsync_ReturnsNull_WhenCacheDisabled()
    {
        var context = BuildContext();
        var store = new TrackingStore();
        var options = Options.Create(new PromptCacheOptions { Enabled = false, DefaultTtl = TimeSpan.FromMinutes(5) });
        var cache = new PromptCache(store, new DefaultPromptCacheKeyBuilder(), options);

        await cache.StoreAsync(context, new LLMResponse { Content = "value" }, CancellationToken.None);
        var setResult = await cache.TryGetAsync(context, CancellationToken.None);

        setResult.Should().BeNull();
        store.SavedEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task TryGetAsync_ReturnsNull_WhenEntryExpired()
    {
        var context = BuildContext();
        var response = new LLMResponse { Content = "stale" };
        var time = new TestTimeProvider();
        var store = new InMemoryPromptCacheStore(time);
        var options = Options.Create(new PromptCacheOptions { Enabled = true, DefaultTtl = TimeSpan.FromMinutes(1) });
        var cache = new PromptCache(store, new DefaultPromptCacheKeyBuilder(), options, time);

        await cache.StoreAsync(context, response, CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(2));
        var result = await cache.TryGetAsync(context, CancellationToken.None);

        result.Should().BeNull();
    }

    private static PromptCacheContext BuildContext()
    {
        var request = new LLMRequest
        {
            ModelId = "claude-3-opus",
            SystemPrompt = "system",
            Temperature = 0.5,
            TopP = 0.9,
            MaxTokens = 1000,
            Messages = new[]
            {
                Message.Create("session", MessageRole.User, new[] { new TextPart("hello cache") })
            },
            Tools = new[]
            {
                new LLMTool
                {
                    Name = "search",
                    Description = "search",
                    Parameters = JsonDocument.Parse("""{"type":"object"}""").RootElement
                }
            }
        };

        return new PromptCacheContext("anthropic", request);
    }

    private sealed class TrackingStore : IPromptCacheStore
    {
        public ConcurrentDictionary<string, PromptCacheEntry> SavedEntries { get; } = new();

        public Task<PromptCacheEntry?> GetAsync(PromptCacheKey key, CancellationToken cancellationToken)
        {
            SavedEntries.TryGetValue(key.Value, out var entry);
            return Task.FromResult(entry);
        }

        public Task SetAsync(PromptCacheEntry entry, CancellationToken cancellationToken)
        {
            SavedEntries[entry.Key.Value] = entry;
            return Task.CompletedTask;
        }
    }
}
