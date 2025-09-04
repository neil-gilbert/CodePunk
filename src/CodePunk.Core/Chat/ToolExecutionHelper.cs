using System.Text;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using CodePunk.Core.Services;
using Microsoft.Extensions.Logging;

namespace CodePunk.Core.Chat;

/// <summary>
/// Helper class for executing tool calls with consistent error handling and timeouts
/// </summary>
internal static class ToolExecutionHelper
{
    /// <summary>
    /// Executes a collection of tool calls and returns the results
    /// </summary>
    public static async Task<List<ToolResultPart>> ExecuteToolCallsAsync(
        IEnumerable<ToolCallPart> toolCalls,
        IToolService toolService,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var toolResultParts = new List<ToolResultPart>();
        
        foreach (var toolCall in toolCalls)
        {
            var result = await ExecuteSingleToolCallAsync(toolCall, toolService, logger, cancellationToken);
            toolResultParts.Add(result);
        }
        
        return toolResultParts;
    }
    
    /// <summary>
    /// Executes a single tool call with timeout and error handling
    /// </summary>
    public static async Task<ToolResultPart> ExecuteSingleToolCallAsync(
        ToolCallPart toolCall,
        IToolService toolService,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Executing tool {ToolName} with call ID {CallId}", toolCall.Name, toolCall.Id);
            
            // Add timeout for tool execution to prevent hanging
            using var toolTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            toolTimeoutCts.CancelAfter(TimeSpan.FromMinutes(2)); // 2 minute timeout per tool
            
            var result = await toolService.ExecuteAsync(toolCall.Name, toolCall.Arguments, toolTimeoutCts.Token)
                .ConfigureAwait(false);
            
            var toolResult = new ToolResultPart(
                toolCall.Id,
                result.Content,
                result.IsError);

            logger.LogInformation("Tool {ToolName} executed successfully", toolCall.Name);
            return toolResult;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // User cancelled, re-throw
            throw;
        }
        catch (OperationCanceledException)
        {
            // Tool timeout
            logger.LogWarning("Tool {ToolName} execution timed out", toolCall.Name);
            
            return new ToolResultPart(
                toolCall.Id,
                "Tool execution timed out after 2 minutes",
                true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing tool {ToolName}", toolCall.Name);
            
            return new ToolResultPart(
                toolCall.Id,
                $"Error executing tool: {ex.Message}",
                true);
        }
    }
    
    /// <summary>
    /// Executes tool calls with streaming status updates
    /// </summary>
    public static async Task<(List<ToolResultPart> Results, List<string> StatusMessages)> ExecuteToolCallsWithStatusAsync(
        IEnumerable<ToolCallPart> toolCalls,
        IToolService toolService,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var toolResultParts = new List<ToolResultPart>();
        var statusMessages = new List<string>();
        
        foreach (var toolCall in toolCalls)
        {
            var (result, statusMessage) = await ExecuteSingleToolCallWithStatusAsync(
                toolCall, toolService, logger, cancellationToken);
            
            toolResultParts.Add(result);
            statusMessages.Add(statusMessage);
        }
        
        return (toolResultParts, statusMessages);
    }
    
    /// <summary>
    /// Executes a single tool call and returns both result and status message
    /// </summary>
    private static async Task<(ToolResultPart Result, string StatusMessage)> ExecuteSingleToolCallWithStatusAsync(
        ToolCallPart toolCall,
        IToolService toolService,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Executing tool {ToolName} with call ID {CallId}", toolCall.Name, toolCall.Id);
            
            // Add timeout for tool execution to prevent hanging
            using var toolTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            toolTimeoutCts.CancelAfter(TimeSpan.FromMinutes(2)); // 2 minute timeout per tool
            
            var result = await toolService.ExecuteAsync(toolCall.Name, toolCall.Arguments, toolTimeoutCts.Token)
                .ConfigureAwait(false);
            
            var toolResult = new ToolResultPart(
                toolCall.Id,
                result.Content,
                result.IsError);

            logger.LogInformation("Tool {ToolName} executed successfully", toolCall.Name);
            
            // Tool completion status
            var statusIcon = result.IsError ? "❌" : "✅";
            var statusMessage = $"\n🔧 Executing {toolCall.Name}... {statusIcon}\n";
            
            return (toolResult, statusMessage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // User cancelled, re-throw
            throw;
        }
        catch (OperationCanceledException)
        {
            // Tool timeout
            logger.LogWarning("Tool {ToolName} execution timed out", toolCall.Name);
            
            var toolResult = new ToolResultPart(
                toolCall.Id,
                "Tool execution timed out after 2 minutes",
                true);
                
            var statusMessage = $"\n🔧 Executing {toolCall.Name}... ⏰ (timed out)\n";
            return (toolResult, statusMessage);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing tool {ToolName}", toolCall.Name);
            
            var toolResult = new ToolResultPart(
                toolCall.Id,
                $"Error executing tool: {ex.Message}",
                true);
                
            var statusMessage = $"\n🔧 Executing {toolCall.Name}... ❌ (error)\n";
            return (toolResult, statusMessage);
        }
    }
}
