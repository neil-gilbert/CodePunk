using System.Text;
using CodePunk.Console.Themes;
using CodePunk.Core.SyntaxHighlighting.Abstractions;
using CodePunk.Core.SyntaxHighlighting.Tokenization;

namespace CodePunk.Console.SyntaxHighlighting;

/// <summary>
/// Builds Spectre markup strings from syntax tokens.
/// </summary>
public sealed class MarkupTokenRenderer : ITokenRenderer
{
    private readonly StringBuilder _builder;

    public MarkupTokenRenderer(StringBuilder builder)
    {
        _builder = builder;
    }

    public void RenderToken(Token token)
    {
        var color = TokenColorPalette.GetColor(token.Type);
        var escaped = ConsoleStyles.Escape(token.Value);

        if (color == "default")
        {
            _builder.Append(escaped);
        }
        else
        {
            _builder.Append('[').Append(color).Append(']').Append(escaped).Append("[/]");
        }
    }
}
