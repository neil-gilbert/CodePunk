using CodePunk.Core.SyntaxHighlighting.Tokenization;

namespace CodePunk.Console.SyntaxHighlighting;

internal static class TokenColorPalette
{
    private static readonly IReadOnlyDictionary<TokenType, string> Colors = new Dictionary<TokenType, string>
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

    public static string GetColor(TokenType tokenType)
        => Colors.TryGetValue(tokenType, out var color) ? color : "default";
}
