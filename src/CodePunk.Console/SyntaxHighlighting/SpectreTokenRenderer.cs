using CodePunk.Console.Themes;
using CodePunk.Core.SyntaxHighlighting.Abstractions;
using CodePunk.Core.SyntaxHighlighting.Tokenization;
using Spectre.Console;

namespace CodePunk.Console.SyntaxHighlighting;

/// <summary>
/// Renders syntax tokens to Spectre.Console with color styling.
/// </summary>
public class SpectreTokenRenderer : ITokenRenderer
{
    private readonly IAnsiConsole _console;

    private static readonly Dictionary<TokenType, string> ColorMap = new()
    {
        [TokenType.Keyword] = "blue",
        [TokenType.Type] = "cyan",
        [TokenType.String] = "green",
        [TokenType.Comment] = "grey",
        [TokenType.Number] = "magenta",
        [TokenType.Operator] = "yellow",
        [TokenType.Punctuation] = "silver",
        [TokenType.Preprocessor] = "purple",
        [TokenType.Identifier] = "white",
        [TokenType.Text] = "default"
    };

    public SpectreTokenRenderer(IAnsiConsole console)
    {
        _console = console;
    }

    public void RenderToken(Token token)
    {
        var color = ColorMap.GetValueOrDefault(token.Type, "default");
        var escaped = ConsoleStyles.Escape(token.Value);

        if (color == "default")
        {
            _console.Markup(escaped);
        }
        else
        {
            _console.Markup($"[{color}]{escaped}[/]");
        }
    }

    public void BeginRender()
    {
        // Optional: Could add a border or background here
    }

    public void EndRender()
    {
        _console.WriteLine(); // Ensure we end with a newline
    }
}
