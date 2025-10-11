namespace CodePunk.Core.Caching;

/// <summary>
/// Configures prompt cache behavior.
/// </summary>
public class PromptCacheOptions
{
    public bool Enabled { get; set; } = true;

    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromMinutes(15);
}
