using Spectre.Console;

namespace CodePunk.Console.Rendering;

public static class JsonOutput
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new() { WriteIndented = true };
    public static void Write(IAnsiConsole console, object payload)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payload, Options);
        console.WriteLine(json);
    }
}
