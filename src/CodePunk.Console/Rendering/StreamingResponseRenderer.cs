using CodePunk.Core.Abstractions;
using CodePunk.Core.Chat;
using CodePunk.Core.Models;
using Spectre.Console;
using System.Text;

namespace CodePunk.Console.Rendering;

/// <summary>
/// Renders streaming AI responses with real-time updates
/// </summary>
public class StreamingResponseRenderer
{
    private readonly IAnsiConsole _console;
    private readonly StringBuilder _currentResponse;
    private bool _isStreaming;

    public StreamingResponseRenderer(IAnsiConsole console)
    {
        _console = console;
        _currentResponse = new StringBuilder();
    }

    /// <summary>
    /// Starts a new streaming response
    /// </summary>
    public void StartStreaming()
    {
        _currentResponse.Clear();
        _isStreaming = true;
        
        // Show AI indicator
        _console.Write(new Rule().LeftJustified());
        _console.MarkupLine("[bold blue]🤖 AI Assistant[/]");
        _console.WriteLine();
    }

    /// <summary>
    /// Processes a stream chunk and updates the display
    /// </summary>
    public void ProcessChunk(ChatStreamChunk chunk)
    {
        if (!_isStreaming)
            return;

        if (chunk.ContentDelta != null)
        {
            _currentResponse.Append(chunk.ContentDelta);
            
            // For now, just write the delta directly
            // In a more sophisticated implementation, we could:
            // - Buffer and format markdown
            // - Handle code blocks with syntax highlighting
            // - Show typing indicators
            _console.Write(chunk.ContentDelta);
        }
    }

    /// <summary>
    /// Completes the streaming response
    /// </summary>
    public void CompleteStreaming()
    {
        if (!_isStreaming)
            return;

        _isStreaming = false;
        _console.WriteLine();
        _console.WriteLine();
    }

    /// <summary>
    /// Renders a complete message (non-streaming)
    /// </summary>
    public void RenderMessage(Message message)
    {
        _console.Write(new Rule().LeftJustified());
        
        // Render message header
        var roleColor = message.Role switch
        {
            MessageRole.User => "green",
            MessageRole.Assistant => "blue", 
            MessageRole.System => "yellow",
            MessageRole.Tool => "purple",
            _ => "white"
        };

        var roleIcon = message.Role switch
        {
            MessageRole.User => "👤",
            MessageRole.Assistant => "🤖",
            MessageRole.System => "⚙️",
            MessageRole.Tool => "🔧",
            _ => "💬"
        };

        _console.MarkupLine($"[bold {roleColor}]{roleIcon} {message.Role}[/]");
        
        if (!string.IsNullOrEmpty(message.Model))
        {
            _console.MarkupLine($"[dim]Model: {message.Model}[/]");
        }
        
        _console.WriteLine();

        // Render message content
        foreach (var part in message.Parts)
        {
            RenderMessagePart(part);
        }

        _console.WriteLine();
    }

    /// <summary>
    /// Renders a single message part
    /// </summary>
    private void RenderMessagePart(MessagePart part)
    {
        switch (part)
        {
            case TextPart textPart:
                RenderText(textPart.Content);
                break;
                
            case ToolCallPart toolCallPart:
                RenderToolCall(toolCallPart);
                break;
                
            case ToolResultPart toolResultPart:
                RenderToolResult(toolResultPart);
                break;
                
            case ImagePart imagePart:
                RenderImage(imagePart);
                break;
                
            default:
                _console.MarkupLine($"[dim]Unknown message part type: {part.GetType().Name}[/]");
                break;
        }
    }

    /// <summary>
    /// Renders text content with basic markdown support
    /// </summary>
    private void RenderText(string content)
    {
        // Basic markdown rendering - can be enhanced later
        var lines = content.Split('\n');
        
        foreach (var line in lines)
        {
            if (line.StartsWith("```"))
            {
                // Code block delimiter
                _console.MarkupLine("[dim]```[/]");
            }
            else if (line.StartsWith("# "))
            {
                // Header
                _console.MarkupLine($"[bold underline]{line[2..]}[/]");
            }
            else if (line.StartsWith("## "))
            {
                // Subheader
                _console.MarkupLine($"[bold]{line[3..]}[/]");
            }
            else if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                // Bullet point
                _console.MarkupLine($"  • {line[2..]}");
            }
            else
            {
                // Regular text
                _console.WriteLine(line);
            }
        }
    }

    /// <summary>
    /// Renders a tool call
    /// </summary>
    private void RenderToolCall(ToolCallPart toolCall)
    {
        _console.MarkupLine($"[purple]🔧 Tool Call: {toolCall.Name}[/]");
        _console.MarkupLine($"[dim]ID: {toolCall.Id}[/]");
        
        // Show arguments if they're reasonable to display
        var argsJson = toolCall.Arguments.ToString();
        if (argsJson.Length < 200)
        {
            _console.MarkupLine($"[dim]Arguments: {argsJson}[/]");
        }
        else
        {
            _console.MarkupLine("[dim]Arguments: [large JSON object][/]");
        }
        _console.WriteLine();
    }

    /// <summary>
    /// Renders a tool result
    /// </summary>
    private void RenderToolResult(ToolResultPart toolResult)
    {
        var statusColor = toolResult.IsError ? "red" : "green";
        var statusIcon = toolResult.IsError ? "❌" : "✅";
        
        _console.MarkupLine($"[{statusColor}]{statusIcon} Tool Result[/]");
        _console.MarkupLine($"[dim]Tool Call ID: {toolResult.ToolCallId}[/]");
        
        // Render the result content
        if (toolResult.IsError)
        {
            _console.MarkupLine($"[red]Error: {toolResult.Content}[/]");
        }
        else
        {
            // Truncate very long results
            var content = toolResult.Content;
            if (content.Length > 1000)
            {
                content = content[..1000] + "\n[... output truncated ...]";
            }
            _console.WriteLine(content);
        }
        _console.WriteLine();
    }

    /// <summary>
    /// Renders an image reference
    /// </summary>
    private void RenderImage(ImagePart image)
    {
        _console.MarkupLine($"[cyan]🖼️ Image: {image.Url}[/]");
        if (!string.IsNullOrEmpty(image.Description))
        {
            _console.MarkupLine($"[dim]Description: {image.Description}[/]");
        }
        _console.WriteLine();
    }
}
