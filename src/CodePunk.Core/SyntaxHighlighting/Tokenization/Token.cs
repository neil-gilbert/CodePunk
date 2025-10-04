namespace CodePunk.Core.SyntaxHighlighting.Tokenization;

/// <summary>
/// Represents a syntax token with its type and value.
/// </summary>
public readonly record struct Token(TokenType Type, string Value);
