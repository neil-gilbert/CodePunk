using System;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using CodePunk.Console.Rendering;
using CodePunk.Console.Themes;
using Spectre.Console;

namespace CodePunk.Console.Utilities;

/// <summary>
/// Provides helper extensions for running asynchronous work behind a Spectre.Console status spinner.
/// </summary>
public static class ConsoleStatusExtensions
{
    /// <summary>
    /// Runs the supplied asynchronous work while displaying a status spinner.
    /// The spinner remains active until the work completes, after which the captured
    /// result is rendered inside a panel and a success message is shown. Exceptions
    /// are surfaced via a red error panel before being rethrown.
    /// </summary>
    /// <param name="console">The console instance.</param>
    /// <param name="statusMessage">The status text to display while work is running.</param>
    /// <param name="work">Asynchronous work returning a string to render when complete.</param>
    /// <param name="successMessage">Optional success message (defaults to a green “✅ Done”).</param>
    /// <returns>The string produced by the work delegate.</returns>
    public static async Task<string> RunWithStatusAsync(
        this IAnsiConsole console,
        string statusMessage,
        Func<Task<string>> work,
        string? successMessage = null)
    {
        if (console == null) throw new ArgumentNullException(nameof(console));
        if (work == null) throw new ArgumentNullException(nameof(work));

        // Honour quiet mode – run the work without UI embellishments.
        if (OutputContext.IsQuiet())
        {
            return await work().ConfigureAwait(false);
        }

        string? result = null;
        Exception? captured = null;

        await console
            .Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(new Style(Color.Aqua))
            .StartAsync(statusMessage, async ctx =>
            {
                try
                {
                    result = await work().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
            })
            .ConfigureAwait(false);

        console.WriteLine();

        if (captured != null)
        {
            var errorPanel = new Panel(new Markup(ConsoleStyles.Error(captured.Message ?? "Unknown error")))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Red),
                Header = new PanelHeader(ConsoleStyles.PanelTitle("Error"))
            };
            console.Write(errorPanel);
            console.WriteLine();
            ExceptionDispatchInfo.Capture(captured).Throw();
        }

        if (!string.IsNullOrWhiteSpace(result))
        {
            var resultPanel = new Panel(new Markup(Markup.Escape(result)))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Aqua),
                Header = new PanelHeader(ConsoleStyles.PanelTitle("Result"))
            };
            console.Write(resultPanel);
            console.WriteLine();
        }

        console.MarkupLine(successMessage ?? "[green]✅ Done[/]");
        console.WriteLine();

        return result ?? string.Empty;
    }
}
