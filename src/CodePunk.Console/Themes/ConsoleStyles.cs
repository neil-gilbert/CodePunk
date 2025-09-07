using Spectre.Console;

namespace CodePunk.Console.Themes;

/// <summary>
/// Central style & color registry for the CLI. Phase 1 provides basic semantic styles.
/// Later phases can add dynamic theming and profiles.
/// </summary>
public static class ConsoleStyles
{
    public const string AccentColor = "deepskyblue1";
    public const string AccentAltColor = "blue";

    public static string Info(string text) => $"[silver]{Escape(text)}[/]";
    public static string Dim(string text) => $"[dim]{Escape(text)}[/]";
    public static string Warn(string text) => $"[yellow]{Escape(text)}[/]";
    public static string Error(string text) => $"[red]{Escape(text)}[/]";
    public static string Success(string text) => $"[green]{Escape(text)}[/]";
    public static string Accent(string text) => $"[{AccentColor}]{Escape(text)}[/]";

    public static string PanelTitle(string text) => $"[{AccentAltColor}]{Escape(text)}[/]";

    public static Rule HeaderRule(string subtitle)
        => new($"{Accent("CodePunk")} {Dim("CLI")} :: {subtitle}")
        {
            Justification = Justify.Left,
            Style = new Style(foreground: Color.Grey),
        };

    public static string Escape(string text) => Markup.Escape(text ?? string.Empty);
}
