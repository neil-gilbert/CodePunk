namespace CodePunk.Core.SyntaxHighlighting.Abstractions;

/// <summary>
/// Main syntax highlighter orchestrator.
/// Coordinates language detection, tokenization, and rendering.
/// </summary>
public interface ISyntaxHighlighter
{
    /// <summary>
    /// Highlights source code in the specified language.
    /// </summary>
    /// <param name="source">Source code to highlight</param>
    /// <param name="languageId">Language identifier (e.g., "csharp", "cs")</param>
    /// <param name="renderer">Renderer to output tokens</param>
    void Highlight(string source, string languageId, ITokenRenderer renderer);

    /// <summary>
    /// Gets all registered language definitions.
    /// </summary>
    IEnumerable<ILanguageDefinition> GetLanguages();
}
