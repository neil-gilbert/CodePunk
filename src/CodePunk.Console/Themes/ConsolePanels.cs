using Spectre.Console;

namespace CodePunk.Console.Themes;

/// <summary>
/// Helper factory for consistent informational panels.
/// Keeps visual language uniform across commands and chat loop.
/// </summary>
public static class ConsolePanels
{
    private static Panel Make(string title, string body, Color border, string? icon = null)
    {
        var header = string.IsNullOrEmpty(icon) ? title : $"{icon} {title}";
        return new Panel(new Markup(body))
            .Header(ConsoleStyles.PanelTitle(header))
            .Border(BoxBorder.Rounded)
            .BorderColor(border)
            .Expand();
    }

    public static Panel Info(string message) => Make("Info", ConsoleStyles.Escape(message), Color.Grey54, "ℹ️");
    public static Panel Success(string message) => Make("Success", ConsoleStyles.Escape(message), Color.Green, "✅");
    public static Panel Warn(string message) => Make("Warning", ConsoleStyles.Escape(message), Color.Yellow, "⚠️");
    public static Panel Error(string message) => Make("Error", ConsoleStyles.Escape(message), Color.Red, "❌");
}
