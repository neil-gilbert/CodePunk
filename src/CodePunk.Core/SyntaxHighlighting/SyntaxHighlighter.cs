using CodePunk.Core.SyntaxHighlighting.Abstractions;

namespace CodePunk.Core.SyntaxHighlighting;

/// <summary>
/// Main syntax highlighter implementation.
/// Coordinates language detection, tokenization, and rendering.
/// </summary>
public class SyntaxHighlighter : ISyntaxHighlighter
{
    private readonly IEnumerable<ILanguageDefinition> _languages;

    public SyntaxHighlighter(IEnumerable<ILanguageDefinition> languages)
    {
        _languages = languages;
    }

    public void Highlight(string source, string languageId, ITokenRenderer renderer)
    {
        if (string.IsNullOrEmpty(source))
            return;

        // Find matching language definition
        var language = _languages.FirstOrDefault(l => l.Matches(languageId));

        if (language == null)
        {
            // No language found - render as plain text
            renderer.BeginRender();
            renderer.RenderToken(new Tokenization.Token(Tokenization.TokenType.Text, source));
            renderer.EndRender();
            return;
        }

        // Tokenize and render
        renderer.BeginRender();
        var tokens = language.Tokenize(source.AsSpan());
        foreach (var token in tokens)
        {
            renderer.RenderToken(token);
        }
        renderer.EndRender();
    }

    public IEnumerable<ILanguageDefinition> GetLanguages() => _languages;
}
