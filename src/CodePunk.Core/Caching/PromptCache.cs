using CodePunk.Core.Abstractions;
using Microsoft.Extensions.Options;

namespace CodePunk.Core.Caching;

/// <summary>
/// Implements prompt cache orchestration.
/// </summary>
public sealed class PromptCache : IPromptCache
{
    private readonly IPromptCacheStore _store;
    private readonly IPromptCacheKeyBuilder _keyBuilder;
    private readonly PromptCacheOptions _options;
    private readonly TimeProvider _timeProvider;

    public PromptCache(
        IPromptCacheStore store,
        IPromptCacheKeyBuilder keyBuilder,
        IOptions<PromptCacheOptions> options,
        TimeProvider? timeProvider = null)
    {
        _store = store;
        _keyBuilder = keyBuilder;
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<PromptCacheEntry?> TryGetAsync(PromptCacheContext context, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return null;
        }

        var key = _keyBuilder.Build(context);
        var entry = await _store.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (entry == null)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        if (entry.IsExpired(now))
        {
            await _store.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return null;
        }

        return entry;
    }

    public async Task StoreAsync(
        PromptCacheContext context,
        bool providerSupportsCache,
        LLMPromptCacheInfo? cacheInfo,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var key = _keyBuilder.Build(context);
        var now = _timeProvider.GetUtcNow();

        DateTimeOffset? expiresAt = cacheInfo?.ExpiresAt;
        if (!expiresAt.HasValue && _options.DefaultTtl > TimeSpan.Zero)
        {
            expiresAt = now.Add(_options.DefaultTtl);
        }

        var entry = new PromptCacheEntry(key, providerSupportsCache, cacheInfo, now, expiresAt);
        await _store.SetAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    public async Task InvalidateAsync(PromptCacheContext context, CancellationToken cancellationToken = default)
    {
        var key = _keyBuilder.Build(context);
        await _store.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
    }
}
