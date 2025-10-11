using Spectre.Console;
using System.Text;

namespace CodePunk.Console.Rendering.Animations;

/// <summary>
/// Creates the animated spinner used while the assistant is thinking or executing tools.
/// </summary>
public static class ThinkingSpinnerFactory
{
    private static readonly string[] Palette =
    [
        "#00E5FF",
        "#3A60FF",
        "#8B3BFF",
        "#FF00C8",
        "#FF5E8A",
        "#FFC53A"
    ];

    private static readonly string[] Frames = BuildFrames("Thinking…", Palette);

    /// <summary>
    /// Gets the spinner instance with precomputed frames.
    /// </summary>
    public static Spinner Spinner { get; } = new("codepunk-thinking", Frames, TimeSpan.FromMilliseconds(120));

    private static string[] BuildFrames(string text, string[] palette)
    {
        var frames = new string[palette.Length];
        for (var offset = 0; offset < palette.Length; offset++)
        {
            var sb = new StringBuilder(text.Length * 10);
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (char.IsWhiteSpace(ch))
                {
                    sb.Append(ch);
                    continue;
                }

                var color = palette[(i + offset) % palette.Length];
                sb.Append('[')
                  .Append(color)
                  .Append(']')
                  .Append(ch)
                  .Append("[/]");
            }
            frames[offset] = sb.ToString();
        }

        return frames;
    }
}
