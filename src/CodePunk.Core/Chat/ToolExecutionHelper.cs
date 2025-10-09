using System.Text;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using CodePunk.Core.Services;
using Microsoft.Extensions.Logging;
using CodePunk.Core.Extensions;
using CodePunk.Core.SyntaxHighlighting;

namespace CodePunk.Core.Chat;

/// <summary>
/// Helper class for executing tool calls with consistent error handling and timeouts
/// </summary>
internal static class ToolExecutionHelper
{
    /// <summary>
    /// Executes a collection of tool calls and returns the results
    /// </summary>
    public static async Task<(List<ToolResultPart> Results, bool UserCancelled)> ExecuteToolCallsAsync(
        IEnumerable<ToolCallPart> toolCalls,
        IToolService toolService,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var toolResultParts = new List<ToolResultPart>();

        foreach (var toolCall in toolCalls)
        {
            var (result, userCancelled) = await ExecuteSingleToolCallWithCancellationAsync(toolCall, toolService, logger, cancellationToken);
            toolResultParts.Add(result);

            // If user cancelled, stop processing further tools
            if (userCancelled)
            {
                return (toolResultParts, true);
            }
        }

        return (toolResultParts, false);
    }
    
    /// <summary>
    /// Executes a single tool call with cancellation detection
    /// </summary>
    private static async Task<(ToolResultPart Result, bool UserCancelled)> ExecuteSingleToolCallWithCancellationAsync(
        ToolCallPart toolCall,
        IToolService toolService,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Executing tool {ToolName} with call ID {CallId}", toolCall.Name, toolCall.Id);

            using var toolTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            toolTimeoutCts.CancelAfter(TimeSpan.FromMinutes(2));

            var result = await toolService.ExecuteAsync(toolCall.Name, toolCall.Arguments, toolTimeoutCts.Token)
                .ConfigureAwait(false);

            var toolResult = new ToolResultPart(
                toolCall.Id,
                result.Content,
                result.IsError);

            logger.LogInformation("Tool {ToolName} executed successfully", toolCall.Name);
            return (toolResult, result.UserCancelled);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Tool {ToolName} execution was cancelled", toolCall.Name);

            return (new ToolResultPart(
                toolCall.Id,
                "Tool execution timed out after 2 minutes",
                true), false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing tool {ToolName}", toolCall.Name);

            return (new ToolResultPart(
                toolCall.Id,
                $"Error executing tool: {ex.Message}",
                true), false);
        }
    }

    private static string BuildStatusPayload(ToolCallPart toolCall, ToolResultPart result)
    {
        var filePath = toolCall.GetFilePath();
        var preview = result.Content.GetLinePreview(ToolResultPreviewLines);
        var languageId = LanguageDetector.FromPath(filePath);

        var payload = new ToolStatusPayload(
            toolCall.Id,
            toolCall.Name,
            filePath,
            preview.Preview,
            preview.IsTruncated,
            preview.OriginalLineCount,
            preview.MaxLines,
            result.IsError,
            result.IsError ? null : languageId);

        return ToolStatusSerializer.Serialize(payload);
    }

    private const int ToolResultPreviewLines = 20;

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
            
            using var toolTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            toolTimeoutCts.CancelAfter(TimeSpan.FromMinutes(2));
            
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
            throw;
        }
        catch (OperationCanceledException)
        {
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
    public static async Task<(List<ToolResultPart> Results, List<string> StatusPayloads, bool UserCancelled)> ExecuteToolCallsWithStatusAsync(
        IEnumerable<ToolCallPart> toolCalls,
        IToolService toolService,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var toolResultParts = new List<ToolResultPart>();
        var statusPayloads = new List<string>();

        foreach (var toolCall in toolCalls)
        {
            var (result, statusMessage, userCancelled) = await ExecuteSingleToolCallWithStatusAndCancellationAsync(
                toolCall, toolService, logger, cancellationToken);

            toolResultParts.Add(result);
            statusPayloads.Add(statusMessage);

            // If user cancelled, stop processing further tools
            if (userCancelled)
            {
                return (toolResultParts, statusPayloads, true);
            }
        }

        return (toolResultParts, statusPayloads, false);
    }
    
    /// <summary>
    /// Executes a single tool call with cancellation detection and status message
    /// </summary>
    private static async Task<(ToolResultPart Result, string StatusMessage, bool UserCancelled)> ExecuteSingleToolCallWithStatusAndCancellationAsync(
        ToolCallPart toolCall,
        IToolService toolService,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Executing tool {ToolName} with call ID {CallId}", toolCall.Name, toolCall.Id);

            using var toolTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            toolTimeoutCts.CancelAfter(TimeSpan.FromMinutes(2));

            var result = await toolService.ExecuteAsync(toolCall.Name, toolCall.Arguments, toolTimeoutCts.Token)
                .ConfigureAwait(false);

            var toolResult = new ToolResultPart(
                toolCall.Id,
                result.Content,
                result.IsError);

            var statusMessage = BuildStatusPayload(toolCall, toolResult);

            logger.LogInformation("Tool {ToolName} executed successfully", toolCall.Name);
            return (toolResult, statusMessage, result.UserCancelled);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Tool {ToolName} execution was cancelled", toolCall.Name);

            var toolResult = new ToolResultPart(
                toolCall.Id,
                "Tool execution timed out after 2 minutes",
                true);

            var statusMessage = BuildStatusPayload(toolCall, toolResult);
            return (toolResult, statusMessage, false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing tool {ToolName}", toolCall.Name);

            var toolResult = new ToolResultPart(
                toolCall.Id,
                $"Error executing tool: {ex.Message}",
                true);

            var statusMessage = BuildStatusPayload(toolCall, toolResult);
            return (toolResult, statusMessage, false);
        }
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
            
            using var toolTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            toolTimeoutCts.CancelAfter(TimeSpan.FromMinutes(2));
            
            var result = await toolService.ExecuteAsync(toolCall.Name, toolCall.Arguments, toolTimeoutCts.Token)
                .ConfigureAwait(false);

            var toolResult = new ToolResultPart(
                toolCall.Id,
                result.Content,
                result.IsError);

            logger.LogInformation("Tool {ToolName} executed successfully", toolCall.Name);
            
            var statusIcon = result.IsError ? "❌" : "✅";
            var statusMessage = string.Empty;
            
            if (Environment.GetEnvironmentVariable("CODEPUNK_VERBOSE") == "1")
            {
                statusMessage = $"\n[tool] {toolCall.Name} {statusIcon}\n";
            }
            
            return (toolResult, statusMessage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Tool {ToolName} execution timed out", toolCall.Name);
            
            var toolResult = new ToolResultPart(
                toolCall.Id,
                "Tool execution timed out after 2 minutes",
                true);
                
            var statusMessage = string.Empty;
            if (Environment.GetEnvironmentVariable("CODEPUNK_VERBOSE") == "1")
            {
                statusMessage = $"\n[tool] {toolCall.Name} ⏰ timeout\n";
            }
            return (toolResult, statusMessage);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing tool {ToolName}", toolCall.Name);
            
            var toolResult = new ToolResultPart(
                toolCall.Id,
                $"Error executing tool: {ex.Message}",
                true);
                
            var statusMessage = string.Empty;
            if (Environment.GetEnvironmentVariable("CODEPUNK_VERBOSE") == "1")
            {
                statusMessage = $"\n[tool] {toolCall.Name} ❌ error\n";
            }
            return (toolResult, statusMessage);
        }
    }
}
