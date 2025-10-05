using System.Text.Json;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Services;
using Microsoft.Extensions.Logging;

namespace CodePunk.Core.GitSession;

public class GitSessionToolInterceptor : IToolService
{
    private readonly IToolService _innerToolService;
    private readonly IGitSessionService _sessionService;
    private readonly ILogger<GitSessionToolInterceptor> _logger;

    private static readonly HashSet<string> ReadOnlyTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "read_file",
        "read_many_files",
        "list_directory",
        "glob",
        "search_files"
    };

    public GitSessionToolInterceptor(
        IToolService innerToolService,
        IGitSessionService sessionService,
        ILogger<GitSessionToolInterceptor> logger)
    {
        _innerToolService = innerToolService;
        _sessionService = sessionService;
        _logger = logger;
    }

    public IReadOnlyList<ITool> GetTools() => _innerToolService.GetTools();

    public ITool? GetTool(string name) => _innerToolService.GetTool(name);

    public IReadOnlyList<LLMTool> GetLLMTools() => _innerToolService.GetLLMTools();

    public async Task<ToolResult> ExecuteAsync(
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        if (!_sessionService.IsEnabled)
        {
            return await _innerToolService.ExecuteAsync(toolName, arguments, cancellationToken);
        }

        try
        {
            // Lazily create git session before first write tool executes
            if (!IsReadOnlyTool(toolName))
            {
                var currentSession = await _sessionService.GetCurrentSessionAsync(cancellationToken);
                if (currentSession == null)
                {
                    _logger.LogInformation("Creating git session before executing write tool {ToolName}", toolName);
                    await _sessionService.BeginSessionAsync(cancellationToken);
                }
            }

            var result = await _innerToolService.ExecuteAsync(toolName, arguments, cancellationToken);

            if (result.IsError || result.UserCancelled)
            {
                await _sessionService.UpdateActivityAsync(cancellationToken);
                return result;
            }

            if (!IsReadOnlyTool(toolName))
            {
                var summary = ExtractSummary(toolName, arguments);
                await _sessionService.CommitToolCallAsync(toolName, summary, cancellationToken);
            }

            await _sessionService.UpdateActivityAsync(cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool execution failed for {ToolName}", toolName);
            await _sessionService.MarkAsFailedAsync($"Tool {toolName} threw exception: {ex.Message}", cancellationToken);
            throw;
        }
    }

    private static bool IsReadOnlyTool(string toolName)
    {
        return ReadOnlyTools.Contains(toolName);
    }

    private static string ExtractSummary(string toolName, JsonElement arguments)
    {
        return toolName switch
        {
            "write_file" when arguments.TryGetProperty("file_path", out var path) =>
                $"Write {path.GetString()}",

            "replace_in_file" when arguments.TryGetProperty("file_path", out var path) =>
                $"Edit {path.GetString()}",

            "run_shell_command" when arguments.TryGetProperty("command", out var cmd) =>
                $"Run: {cmd.GetString()?.Split(' ')[0] ?? "command"}",

            _ => toolName
        };
    }
}
