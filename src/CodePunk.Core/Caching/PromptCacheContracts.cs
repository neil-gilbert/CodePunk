using CodePunk.Core.Abstractions;

namespace CodePunk.Core.Caching;

/// <summary>
/// Represents a request context for prompt caching.
/// </summary>
public sealed record PromptCacheContext(string ProviderName, LLMRequest Request);

/// <summary>
/// Represents a cache key for provider-side prompt caches.
/// </summary>
public readonly record struct PromptCacheKey(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// Represents a stored prompt cache entry referencing a provider cache identifier.
/// </summary>
public sealed record PromptCacheEntry(
    PromptCacheKey Key,
    bool ProviderSupportsCache,
    LLMPromptCacheInfo? CacheInfo,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt)
{
    public bool IsExpired(DateTimeOffset timestamp) =>
        ExpiresAt.HasValue && timestamp >= ExpiresAt.Value;
}

/// <summary>
/// Defines prompt cache orchestration.
/// </summary>
public interface IPromptCache
{
    Task<PromptCacheEntry?> TryGetAsync(PromptCacheContext context, CancellationToken cancellationToken = default);

    Task StoreAsync(
        PromptCacheContext context,
        bool providerSupportsCache,
        LLMPromptCacheInfo? cacheInfo,
        CancellationToken cancellationToken = default);

    Task InvalidateAsync(PromptCacheContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines a builder for prompt cache keys.
/// </summary>
public interface IPromptCacheKeyBuilder
{
    PromptCacheKey Build(PromptCacheContext context);
}

/// <summary>
/// Defines a storage primitive for prompt cache entries.
/// </summary>
public interface IPromptCacheStore
{
    Task<PromptCacheEntry?> GetAsync(PromptCacheKey key, CancellationToken cancellationToken);

    Task SetAsync(PromptCacheEntry entry, CancellationToken cancellationToken);

    Task RemoveAsync(PromptCacheKey key, CancellationToken cancellationToken);
}
