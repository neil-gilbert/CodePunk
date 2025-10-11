using System.Collections.Concurrent;

namespace CodePunk.Core.Caching;

/// <summary>
/// Provides an in-memory prompt cache store.
/// </summary>
public sealed class InMemoryPromptCacheStore : IPromptCacheStore
{
    private readonly ConcurrentDictionary<string, PromptCacheEntry> _entries = new();
    private readonly TimeProvider _timeProvider;

    public InMemoryPromptCacheStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<PromptCacheEntry?> GetAsync(PromptCacheKey key, CancellationToken cancellationToken)
    {
        if (_entries.TryGetValue(key.Value, out var entry))
        {
            if (entry.IsExpired(_timeProvider.GetUtcNow()))
            {
                _entries.TryRemove(key.Value, out _);
                return Task.FromResult<PromptCacheEntry?>(null);
            }

            return Task.FromResult<PromptCacheEntry?>(entry);
        }

        return Task.FromResult<PromptCacheEntry?>(null);
    }

    public Task SetAsync(PromptCacheEntry entry, CancellationToken cancellationToken)
    {
        _entries[entry.Key.Value] = entry;
        return Task.CompletedTask;
    }
}
