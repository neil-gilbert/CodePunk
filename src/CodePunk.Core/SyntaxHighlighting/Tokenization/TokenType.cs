namespace CodePunk.Core.SyntaxHighlighting.Tokenization;

/// <summary>
/// Token types for syntax highlighting.
/// </summary>
public enum TokenType
{
    /// <summary>Default text</summary>
    Text,

    /// <summary>Language keywords (class, if, return, etc.)</summary>
    Keyword,

    /// <summary>Built-in types (int, string, etc.)</summary>
    Type,

    /// <summary>String literals</summary>
    String,

    /// <summary>Single or multi-line comments</summary>
    Comment,

    /// <summary>Numeric literals</summary>
    Number,

    /// <summary>Operators (+, -, =, etc.)</summary>
    Operator,

    /// <summary>Punctuation ({, }, (, ), ;, etc.)</summary>
    Punctuation,

    /// <summary>Variable and method names</summary>
    Identifier,

    /// <summary>Preprocessor directives (#if, #define, etc.)</summary>
    Preprocessor
}
