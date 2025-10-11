using CodePunk.Core.Abstractions;

namespace CodePunk.Core.Caching;

/// <summary>
/// Represents a request context for prompt caching.
/// </summary>
public sealed record PromptCacheContext(string ProviderName, LLMRequest Request);

/// <summary>
/// Represents a cache key for prompt responses.
/// </summary>
public readonly record struct PromptCacheKey(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// Represents a stored prompt cache entry.
/// </summary>
public sealed record PromptCacheEntry(
    PromptCacheKey Key,
    PromptCacheContext Context,
    LLMResponse Response,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt)
{
    public bool IsExpired(DateTimeOffset now) => ExpiresAt.HasValue && now >= ExpiresAt.Value;
}

/// <summary>
/// Represents a prompt cache lookup result.
/// </summary>
public sealed record PromptCacheResult(PromptCacheKey Key, LLMResponse Response);

/// <summary>
/// Defines prompt cache orchestration.
/// </summary>
public interface IPromptCache
{
    Task<PromptCacheResult?> TryGetAsync(PromptCacheContext context, CancellationToken cancellationToken = default);

    Task StoreAsync(PromptCacheContext context, LLMResponse response, CancellationToken cancellationToken = default);
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
}
