using System.Text;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;

namespace CodePunk.Core.Chat;

/// <summary>
/// Helper for processing AI responses and extracting content and tool calls
/// </summary>
internal static class AIResponseProcessor
{
    /// <summary>
    /// Processes streaming chunks and extracts content and tool calls
    /// </summary>
    public static async Task<(string Content, List<ToolCallPart> ToolCalls)> ProcessStreamingResponseAsync(
        IAsyncEnumerable<LLMStreamChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        var responseContent = new StringBuilder();
        var toolCalls = new List<ToolCallPart>();
        
        await foreach (var chunk in chunks.WithCancellation(cancellationToken))
        {
            if (chunk.Content != null)
            {
                responseContent.Append(chunk.Content);
            }
            
            if (chunk.ToolCall != null)
            {
                toolCalls.Add(new ToolCallPart(chunk.ToolCall.Id, chunk.ToolCall.Name, chunk.ToolCall.Arguments));
            }
        }
        
        return (responseContent.ToString(), toolCalls);
    }
    
    /// <summary>
    /// Creates message parts from content and tool calls
    /// </summary>
    public static List<MessagePart> CreateMessageParts(string content, IEnumerable<ToolCallPart> toolCalls)
    {
        var responseParts = new List<MessagePart>();
        
        if (!string.IsNullOrEmpty(content))
        {
            responseParts.Add(new TextPart(content));
        }
        
        responseParts.AddRange(toolCalls);
        
        return responseParts;
    }
    
    /// <summary>
    /// Creates an AI message with the given content and tool calls
    /// </summary>
    public static Message CreateAIMessage(
        string sessionId, 
        string content, 
        IEnumerable<ToolCallPart> toolCalls,
        string model,
        string provider)
    {
        var parts = CreateMessageParts(content, toolCalls);
        
        return Message.Create(
            sessionId,
            MessageRole.Assistant,
            parts,
            model,
            provider);
    }
    
    /// <summary>
    /// Creates a tool results message
    /// </summary>
    public static Message CreateToolResultsMessage(string sessionId, IEnumerable<ToolResultPart> toolResults)
    {
        return Message.Create(
            sessionId,
            MessageRole.User,
            toolResults.Cast<MessagePart>().ToList());
    }
    
    /// <summary>
    /// Creates a fallback message when max iterations are exceeded
    /// </summary>
    public static Message CreateFallbackMessage(string sessionId, string model, string provider)
    {
        const string fallbackContent = "I apologize, but I encountered too many tool calls and had to stop to prevent an infinite loop. Please try a simpler request or contact support if this continues.";
        
        return Message.Create(
            sessionId,
            MessageRole.Assistant,
            [new TextPart(fallbackContent)],
            model,
            provider);
    }
}
