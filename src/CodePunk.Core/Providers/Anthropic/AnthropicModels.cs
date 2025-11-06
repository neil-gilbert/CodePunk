using System.Collections.Generic;

namespace CodePunk.Core.Providers.Anthropic;

public static class AnthropicModels
{
    // Latest 5 primary Anthropic model snapshots (Sept 2025)
    public const string ClaudeOpus41 = "claude-opus-4-1-20250805";
    public const string ClaudeOpus4 = "claude-opus-4-20250514";
    public const string ClaudeSonnet4 = "claude-sonnet-4-20250514";
    public const string ClaudeSonnet45 = "claude-sonnet-4-5";
    public const string Claude37Sonnet = "claude-3-7-sonnet-20250219";
    public const string Claude35Haiku = "claude-3-5-haiku-20241022";

    public static readonly Dictionary<string, ModelCapabilities> Capabilities = new()
    {
        [ClaudeOpus41] = new(MaxTokens: 200000, SupportsStreaming: true, SupportsTools: true),
        [ClaudeOpus4] = new(MaxTokens: 200000, SupportsStreaming: true, SupportsTools: true),
        [ClaudeSonnet4] = new(MaxTokens: 200000, SupportsStreaming: true, SupportsTools: true), // 1M beta context available via header
        [ClaudeSonnet45] = new(MaxTokens: 200000, SupportsStreaming: true, SupportsTools: true),
        [Claude37Sonnet] = new(MaxTokens: 200000, SupportsStreaming: true, SupportsTools: true),
        [Claude35Haiku] = new(MaxTokens: 200000, SupportsStreaming: true, SupportsTools: true)
    };
}

public record ModelCapabilities(int MaxTokens, bool SupportsStreaming, bool SupportsTools);
