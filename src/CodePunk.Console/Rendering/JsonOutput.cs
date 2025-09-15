using Spectre.Console;

namespace CodePunk.Console.Rendering;

public static class JsonOutput
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new() { WriteIndented = true };
    public static void Write(IAnsiConsole console, object payload)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payload, Options);
        try
        {
            // Write to the provided IAnsiConsole (used by Spectre.Console test harness)
            console.WriteLine(json);
        }
        catch
        {
            // Ignore if console implementation doesn't support WriteLine
        }
        // Also write to the system console output so tests that redirect Console.Out capture JSON
        System.Console.WriteLine(json);
    }
}
