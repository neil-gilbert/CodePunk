namespace CodePunk.Console.Rendering;

/// <summary>
/// Options for configuring streaming response rendering behavior.
/// Live mode is a future enhancement; currently only toggles header label.
/// </summary>
public class StreamingRendererOptions
{
    /// <summary>
    /// Enable experimental live panel rendering (placeholder for future implementation).
    /// </summary>
    public bool LiveEnabled { get; set; } = false;
}
