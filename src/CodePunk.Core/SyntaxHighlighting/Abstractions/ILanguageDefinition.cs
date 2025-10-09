using CodePunk.Core.SyntaxHighlighting.Tokenization;

namespace CodePunk.Core.SyntaxHighlighting.Abstractions;

/// <summary>
/// Defines a language grammar for syntax highlighting.
/// Inspired by highlight.js language definitions.
/// </summary>
public interface ILanguageDefinition
{
    /// <summary>
    /// Primary language name (e.g., "csharp")
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Alternative identifiers for this language (e.g., ["cs", "c#"])
    /// </summary>
    string[] Aliases { get; }

    /// <summary>
    /// Checks if this language can handle the given language identifier.
    /// </summary>
    bool Matches(string languageId);

    /// <summary>
    /// Tokenizes source code into syntax tokens.
    /// Uses ReadOnlySpan for performance (zero allocations).
    /// </summary>
    IEnumerable<Token> Tokenize(ReadOnlySpan<char> source);
}
