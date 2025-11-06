using System.Text.Json;
using CodePunk.Core.Models;

namespace CodePunk.Core.Abstractions;

/// <summary>
/// Provider for LLM services (OpenAI, Anthropic, etc.)
/// </summary>
public interface ILLMProvider
{
    /// <summary>
    /// The name of this provider (e.g., "OpenAI", "Anthropic")
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Available models for this provider
    /// </summary>
    IReadOnlyList<LLMModel> Models { get; }

    /// <summary>
    /// Optionally fetch live model list from the provider. Implementations may return an empty list or throw on error.
    /// </summary>
    Task<IReadOnlyList<LLMModel>> FetchModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a non-streaming request to the LLM
    /// </summary>
    Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a streaming request to the LLM
    /// </summary>
    IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration for LLM providers
/// </summary>
public record LLMProviderConfig
{
    public required string Name { get; init; }
    public required string ApiKey { get; init; }
    public string? BaseUrl { get; init; }
    public Dictionary<string, string> ExtraHeaders { get; init; } = new();
    public Dictionary<string, object> ExtraParameters { get; init; } = new();
}

/// <summary>
/// Represents an LLM model
/// </summary>
public record LLMModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public int MaxTokens { get; init; } = 4096;
    public int ContextWindow { get; init; } = 4096;
    public decimal CostPerInputToken { get; init; }
    public decimal CostPerOutputToken { get; init; }
    public bool SupportsTools { get; init; } = true;
    public bool SupportsStreaming { get; init; } = true;
}

/// <summary>
/// Request to an LLM provider
/// </summary>
public record LLMRequest
{
    public required string ModelId { get; init; }
    public required IReadOnlyList<Message> Messages { get; init; }
    public IReadOnlyList<LLMTool>? Tools { get; init; }
    public string? SystemPrompt { get; init; }
    public int MaxTokens { get; init; } = 1000;
    public double Temperature { get; init; } = 0.2;
    public double TopP { get; init; } = 1.0;
    public bool UseEphemeralCache { get; init; }

    /// <summary>
    /// Optional structured-output configuration, enabling providers to return strict JSON or JSON matching a schema.
    /// When supported, providers will use native response formatting (e.g., OpenAI response_format) or prompt guidance.
    public string[]? Stop { get; init; }
    public int? Seed { get; init; }
    public double? FrequencyPenalty { get; init; }
    public double? PresencePenalty { get; init; }
    /// <summary>
    /// Provider hint for tool invocation behavior: "auto" | "required" | "none"
    /// </summary>
    public string? ToolChoice { get; init; }
    /// <summary>
    /// Optional structured-output configuration (e.g., JSON/JSON schema)
    /// </summary>
    public LLMResponseFormat? ResponseFormat { get; init; }
}

/// <summary>
/// Response from an LLM provider
/// </summary>
public record LLMResponse
{
    public required string Content { get; init; }
    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }
    public LLMUsage? Usage { get; init; }
    public LLMFinishReason FinishReason { get; init; } = LLMFinishReason.Stop;
    public LLMPromptCacheInfo? PromptCache { get; init; }
}

/// <summary>
/// Streaming chunk from an LLM provider
/// </summary>
public record LLMStreamChunk
{
    public string? Content { get; init; }
    public ToolCall? ToolCall { get; init; }
    public LLMUsage? Usage { get; init; }
    public LLMFinishReason? FinishReason { get; init; }
    public bool IsComplete { get; init; }
    public LLMPromptCacheInfo? PromptCache { get; init; }
    public string? EventType { get; init; }
    public ServerToolUseEvent? ServerToolUse { get; init; }
    public McpToolUseEvent? McpToolUse { get; init; }
    public WebSearchToolResultEvent? WebSearchToolResult { get; init; }
}

/// <summary>
/// Response formatting preferences. For OpenAI, maps to response_format.
/// If Type == "json_schema", a JsonSchema should be provided; SchemaName may be required by some providers.
/// </summary>
public sealed record LLMResponseFormat
{
    /// <summary>
    /// "text" | "json_object" | "json_schema"
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Optional schema name for providers that require a name for the schema (e.g., OpenAI json_schema.name).
    /// </summary>
    public string? SchemaName { get; init; }

    /// <summary>
    /// Raw JSON schema when Type == "json_schema".
    /// </summary>
    public JsonElement? JsonSchema { get; init; }
}

public record ServerToolUseEvent(string Id, string Name, string? Query);

public record McpToolUseEvent(string Id, string Name, string? ServerName, JsonElement Input);

public record WebSearchToolResultEvent(string? ToolUseId, bool? IsError, JsonElement? Content);

/// <summary>
/// Tool definition for LLM function calling
/// </summary>
public record LLMTool
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public JsonElement Parameters { get; init; }
}

/// <summary>
/// Tool call from LLM
/// </summary>
public record ToolCall
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public JsonElement Arguments { get; init; }
}

/// <summary>
/// Usage statistics from LLM response
/// </summary>
public record LLMUsage
{
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int TotalTokens => InputTokens + OutputTokens;
    public decimal EstimatedCost { get; init; }
}

/// <summary>
/// Reason why LLM finished generating
/// </summary>
public enum LLMFinishReason
{
    Stop,
    MaxTokens,
    ToolCall,
    ContentFilter,
    Error
}
/// <summary>
/// Information about provider-side prompt cache entries.
/// </summary>
public record LLMPromptCacheInfo
{
    public required string CacheId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}
