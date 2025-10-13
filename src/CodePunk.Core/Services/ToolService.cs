using System.Text.Json;
using CodePunk.Core.Abstractions;

namespace CodePunk.Core.Services;

/// <summary>
/// Service for managing and executing tools
/// </summary>
public interface IToolService
{
    /// <summary>
    /// Get all available tools
    /// </summary>
    IReadOnlyList<ITool> GetTools();

    /// <summary>
    /// Get a specific tool by name
    /// </summary>
    ITool? GetTool(string name);

    /// <summary>
    /// Execute a tool with the given arguments
    /// </summary>
    Task<ToolResult> ExecuteAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken = default);

    /// <summary>
    /// Convert tools to LLM tool definitions
    /// </summary>
    IReadOnlyList<LLMTool> GetLLMTools();
}

/// <summary>
/// Base interface for all tools
/// </summary>
public interface ITool
{
    /// <summary>
    /// The name of this tool
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Description of what this tool does
    /// </summary>
    string Description { get; }

    /// <summary>
    /// JSON schema for tool parameters
    /// </summary>
    JsonElement Parameters { get; }

    /// <summary>
    /// Execute this tool with the given arguments
    /// </summary>
    Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result from tool execution
/// </summary>
public record ToolResult
{
    public required string Content { get; init; }
    public bool IsError { get; init; } = false;
    public string? ErrorMessage { get; init; }
    public bool UserCancelled { get; init; } = false;
}

/// <summary>
/// Implementation of tool service
/// </summary>
public class ToolService : IToolService
{
    private readonly IReadOnlyList<ITool> _tools;

    public ToolService(IEnumerable<ITool> tools)
    {
        _tools = tools.ToList();
    }

    public IReadOnlyList<ITool> GetTools() => _tools;

    public ITool? GetTool(string name) =>
        _tools.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public async Task<ToolResult> ExecuteAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var tool = GetTool(toolName);
        if (tool == null)
        {
            return new ToolResult
            {
                Content = $"Tool '{toolName}' not found",
                IsError = true,
                ErrorMessage = $"Unknown tool: {toolName}"
            };
        }

        try
        {
            return await tool.ExecuteAsync(arguments, cancellationToken);
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                Content = $"Tool execution failed: {ex.Message}",
                IsError = true,
                ErrorMessage = ex.Message
            };
        }
    }

    public IReadOnlyList<LLMTool> GetLLMTools()
    {
        var compact = string.Equals(Environment.GetEnvironmentVariable("CODEPUNK_COMPACT_TOOLS"), "1", StringComparison.Ordinal);
        return _tools.Select(tool => new LLMTool
        {
            Name = tool.Name,
            Description = compact ? TrimDescription(tool.Description) : tool.Description,
            Parameters = tool.Parameters
        }).ToList();
    }

    private static string TrimDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return description;
        // Use first sentence or cap to 140 chars for compact mode
        var firstStop = description.IndexOf('.', StringComparison.Ordinal);
        var first = firstStop > 0 ? description[..(firstStop + 1)] : description;
        var trimmed = first.Trim();
        if (trimmed.Length > 140) trimmed = trimmed[..140].TrimEnd() + "…";
        return trimmed;
    }
}
