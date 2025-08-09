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

    public IReadOnlyList<LLMTool> GetLLMTools() =>
        _tools.Select(tool => new LLMTool
        {
            Name = tool.Name,
            Description = tool.Description,
            Parameters = tool.Parameters
        }).ToList();
}
