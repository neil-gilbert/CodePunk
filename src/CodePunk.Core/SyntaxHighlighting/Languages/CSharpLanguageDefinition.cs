using CodePunk.Core.SyntaxHighlighting.Abstractions;
using CodePunk.Core.SyntaxHighlighting.Tokenization;

namespace CodePunk.Core.SyntaxHighlighting.Languages;

/// <summary>
/// C# language definition for syntax highlighting.
/// Implements a simple state machine tokenizer.
/// </summary>
public class CSharpLanguageDefinition : ILanguageDefinition
{
    public string Name => "csharp";
    public string[] Aliases => new[] { "cs", "c#", "dotnet" };

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "break", "case", "catch",
        "checked", "class", "const", "continue", "default", "delegate",
        "do", "else", "enum", "event", "explicit", "extern", "false",
        "finally", "fixed", "for", "foreach", "goto", "if", "implicit",
        "in", "interface", "internal", "is", "lock", "namespace",
        "new", "null", "operator", "out", "override", "params", "private",
        "protected", "public", "readonly", "ref", "return", "sealed",
        "sizeof", "stackalloc", "static", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "unchecked",
        "unsafe", "using", "virtual", "volatile", "while",
        "async", "await", "nameof", "when", "partial", "yield",
        "get", "set", "add", "remove", "value", "global", "record", "init", "with",
        "required", "scoped", "file", "var", "dynamic"
    };

    private static readonly HashSet<string> BuiltInTypes = new(StringComparer.Ordinal)
    {
        "int", "string", "bool", "void", "object", "byte", "sbyte", "short",
        "ushort", "uint", "long", "ulong", "float", "double", "decimal", "char",
        "nint", "nuint"
    };

    public bool Matches(string languageId)
    {
        if (string.IsNullOrWhiteSpace(languageId)) return false;
        var normalized = languageId.ToLowerInvariant();
        return normalized == Name || Aliases.Contains(normalized);
    }

    public IEnumerable<Token> Tokenize(ReadOnlySpan<char> source)
    {
        var tokens = new List<Token>();
        var pos = 0;

        while (pos < source.Length)
        {
            var ch = source[pos];

            // Skip whitespace
            if (char.IsWhiteSpace(ch))
            {
                var start = pos;
                while (pos < source.Length && char.IsWhiteSpace(source[pos]))
                    pos++;
                tokens.Add(new Token(TokenType.Text, source.Slice(start, pos - start).ToString()));
                continue;
            }

            // Single-line comment
            if (ch == '/' && pos + 1 < source.Length && source[pos + 1] == '/')
            {
                var start = pos;
                pos += 2;
                while (pos < source.Length && source[pos] != '\n')
                    pos++;
                tokens.Add(new Token(TokenType.Comment, source.Slice(start, pos - start).ToString()));
                continue;
            }

            // Multi-line comment
            if (ch == '/' && pos + 1 < source.Length && source[pos + 1] == '*')
            {
                var start = pos;
                pos += 2;
                while (pos < source.Length - 1)
                {
                    if (source[pos] == '*' && source[pos + 1] == '/')
                    {
                        pos += 2;
                        break;
                    }
                    pos++;
                }
                tokens.Add(new Token(TokenType.Comment, source.Slice(start, pos - start).ToString()));
                continue;
            }

            // String literals (basic - handles "" but not verbatim or interpolated yet)
            if (ch == '"')
            {
                var start = pos;
                pos++;
                while (pos < source.Length)
                {
                    if (source[pos] == '\\' && pos + 1 < source.Length)
                    {
                        pos += 2; // Skip escaped character
                        continue;
                    }
                    if (source[pos] == '"')
                    {
                        pos++;
                        break;
                    }
                    pos++;
                }
                tokens.Add(new Token(TokenType.String, source.Slice(start, pos - start).ToString()));
                continue;
            }

            // Char literals
            if (ch == '\'')
            {
                var start = pos;
                pos++;
                while (pos < source.Length)
                {
                    if (source[pos] == '\\' && pos + 1 < source.Length)
                    {
                        pos += 2;
                        continue;
                    }
                    if (source[pos] == '\'')
                    {
                        pos++;
                        break;
                    }
                    pos++;
                }
                tokens.Add(new Token(TokenType.String, source.Slice(start, pos - start).ToString()));
                continue;
            }

            // Preprocessor directives
            if (ch == '#')
            {
                var start = pos;
                while (pos < source.Length && source[pos] != '\n')
                    pos++;
                tokens.Add(new Token(TokenType.Preprocessor, source.Slice(start, pos - start).ToString()));
                continue;
            }

            // Numbers
            if (char.IsDigit(ch))
            {
                var start = pos;
                while (pos < source.Length && (char.IsDigit(source[pos]) || source[pos] == '.' ||
                       source[pos] == 'f' || source[pos] == 'd' || source[pos] == 'm' ||
                       source[pos] == 'l' || source[pos] == 'u' || source[pos] == 'L' ||
                       source[pos] == 'U' || source[pos] == 'x' || source[pos] == 'X'))
                    pos++;
                tokens.Add(new Token(TokenType.Number, source.Slice(start, pos - start).ToString()));
                continue;
            }

            // Identifiers and keywords
            if (char.IsLetter(ch) || ch == '_' || ch == '@')
            {
                var start = pos;
                if (ch == '@') pos++; // Skip @ for verbatim identifiers
                while (pos < source.Length && (char.IsLetterOrDigit(source[pos]) || source[pos] == '_'))
                    pos++;

                var text = source.Slice(start, pos - start).ToString();
                var word = text.TrimStart('@');

                TokenType type = TokenType.Identifier;
                if (BuiltInTypes.Contains(word))
                    type = TokenType.Type;
                else if (Keywords.Contains(word))
                    type = TokenType.Keyword;

                tokens.Add(new Token(type, text));
                continue;
            }

            // Operators (multi-char)
            if (IsOperatorStart(ch))
            {
                var start = pos;
                pos++;
                // Check for multi-char operators
                while (pos < source.Length && IsOperatorPart(source[pos]))
                    pos++;
                tokens.Add(new Token(TokenType.Operator, source.Slice(start, pos - start).ToString()));
                continue;
            }

            // Punctuation
            if (IsPunctuation(ch))
            {
                tokens.Add(new Token(TokenType.Punctuation, ch.ToString()));
                pos++;
                continue;
            }

            // Unknown character - treat as text
            tokens.Add(new Token(TokenType.Text, ch.ToString()));
            pos++;
        }

        return tokens;
    }

    private static bool IsOperatorStart(char ch) =>
        ch == '+' || ch == '-' || ch == '*' || ch == '/' || ch == '%' ||
        ch == '=' || ch == '!' || ch == '<' || ch == '>' || ch == '&' ||
        ch == '|' || ch == '^' || ch == '~' || ch == '?' || ch == ':';

    private static bool IsOperatorPart(char ch) =>
        ch == '+' || ch == '-' || ch == '=' || ch == '&' || ch == '|' ||
        ch == '<' || ch == '>' || ch == '?';

    private static bool IsPunctuation(char ch) =>
        ch == '{' || ch == '}' || ch == '(' || ch == ')' || ch == '[' || ch == ']' ||
        ch == ';' || ch == ',' || ch == '.';
}
