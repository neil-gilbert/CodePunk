using CodePunk.Core.SyntaxHighlighting.Tokenization;

namespace CodePunk.Core.SyntaxHighlighting.Abstractions;

/// <summary>
/// Abstraction for rendering syntax tokens to output.
/// Allows different renderers (console, HTML, etc.)
/// </summary>
public interface ITokenRenderer
{
    /// <summary>
    /// Renders a single token with appropriate styling.
    /// </summary>
    void RenderToken(Token token);

    /// <summary>
    /// Called before rendering a sequence of tokens (optional setup).
    /// </summary>
    void BeginRender() { }

    /// <summary>
    /// Called after rendering a sequence of tokens (optional cleanup).
    /// </summary>
    void EndRender() { }
}
