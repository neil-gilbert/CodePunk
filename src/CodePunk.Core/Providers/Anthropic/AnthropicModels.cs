using System.Collections.Generic;

namespace CodePunk.Core.Providers.Anthropic;

public static class AnthropicModels
{
    public const string Claude35Sonnet = "claude-3-5-sonnet-20241022";
    public const string Claude35Haiku = "claude-3-5-haiku-20241022";
    public const string Claude3Opus = "claude-3-opus-20240229";
    public const string Claude3Sonnet = "claude-3-sonnet-20240229";
    public const string Claude3Haiku = "claude-3-haiku-20240307";
    
    public static readonly Dictionary<string, ModelCapabilities> Capabilities = new()
    {
        [Claude35Sonnet] = new(MaxTokens: 200000, SupportsStreaming: true, SupportsTools: true),
        [Claude35Haiku] = new(MaxTokens: 200000, SupportsStreaming: true, SupportsTools: true),
        [Claude3Opus] = new(MaxTokens: 200000, SupportsStreaming: true, SupportsTools: true),
        [Claude3Sonnet] = new(MaxTokens: 200000, SupportsStreaming: true, SupportsTools: true),
        [Claude3Haiku] = new(MaxTokens: 200000, SupportsStreaming: true, SupportsTools: false)
    };
}

public record ModelCapabilities(int MaxTokens, bool SupportsStreaming, bool SupportsTools);
