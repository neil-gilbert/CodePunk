namespace CodePunk.Core.Chat;

/// <summary>
/// Configuration options for chat sessions
/// </summary>
public interface IChatSessionOptions
{
    /// <summary>
    /// Maximum number of tool calling iterations to prevent infinite loops
    /// </summary>
    int MaxToolCallIterations { get; }
    
    /// <summary>
    /// Timeout for individual tool execution
    /// </summary>
    TimeSpan ToolExecutionTimeout { get; }
    
    /// <summary>
    /// Default model name when not specified
    /// </summary>
    string DefaultModel { get; }
    
    /// <summary>
    /// Default provider name when not specified
    /// </summary>
    string DefaultProvider { get; }
}

/// <summary>
/// Default implementation of chat session options
/// </summary>
public class ChatSessionOptions : IChatSessionOptions
{
    /// <summary>
    /// Maximum number of tool calling iterations to prevent infinite loops
    /// </summary>
    public int MaxToolCallIterations { get; set; } = 5;
    
    /// <summary>
    /// Timeout for individual tool execution
    /// </summary>
    public TimeSpan ToolExecutionTimeout { get; set; } = TimeSpan.FromMinutes(2);
    
    /// <summary>
    /// Default model name when not specified
    /// </summary>
    public string DefaultModel { get; set; } = "claude-3-5-sonnet";
    
    /// <summary>
    /// Default provider name when not specified
    /// </summary>
    public string DefaultProvider { get; set; } = "Anthropic";
}
