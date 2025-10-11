namespace CodePunk.Console.Rendering.Animations;

/// <summary>
/// Options controlling console animations and status spinners.
/// </summary>
public class ConsoleAnimationOptions
{
    /// <summary>
    /// Enables the animated status indicator shown while the assistant is thinking or executing tools.
    /// </summary>
    public bool EnableStatusAnimation { get; set; } = true;
}
