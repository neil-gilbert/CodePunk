using CodePunk.Core.SyntaxHighlighting.Abstractions;
using CodePunk.Core.SyntaxHighlighting.Tokenization;

namespace CodePunk.Core.SyntaxHighlighting.Languages;

/// <summary>
/// Go language definition for syntax highlighting.
/// Tokenizer handles comments, literals, numbers, and identifiers.
/// </summary>
public class GoLanguageDefinition : ILanguageDefinition
{
    public string Name => "go";
    public string[] Aliases => new[] { "golang" };

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "break", "case", "chan", "const", "continue", "default", "defer",
        "else", "fallthrough", "for", "func", "go", "goto", "if", "import",
        "interface", "map", "package", "range", "return", "select",
        "struct", "switch", "type", "var"
    };

    private static readonly HashSet<string> BuiltInTypes = new(StringComparer.Ordinal)
    {
        "bool", "byte", "complex64", "complex128", "error", "float32", "float64",
        "int", "int8", "int16", "int32", "int64", "rune", "string",
        "uint", "uint8", "uint16", "uint32", "uint64", "uintptr"
    };

    private static readonly HashSet<string> BuiltInConstants = new(StringComparer.Ordinal)
    {
        "true", "false", "iota", "nil"
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

            if (char.IsWhiteSpace(ch))
            {
                var start = pos;
                while (pos < source.Length && char.IsWhiteSpace(source[pos]))
                    pos++;
                tokens.Add(new Token(TokenType.Text, source[start..pos].ToString()));
                continue;
            }

            if (ch == '/' && pos + 1 < source.Length)
            {
                if (source[pos + 1] == '/')
                {
                    var start = pos;
                    pos += 2;
                    while (pos < source.Length && source[pos] != '\n')
                        pos++;
                    tokens.Add(new Token(TokenType.Comment, source[start..pos].ToString()));
                    continue;
                }

                if (source[pos + 1] == '*')
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
                    tokens.Add(new Token(TokenType.Comment, source[start..pos].ToString()));
                    continue;
                }
            }

            if (ch == '"' || ch == '`')
            {
                tokens.Add(ParseString(source, ref pos, ch));
                continue;
            }

            if (ch == '\'')
            {
                tokens.Add(ParseRune(source, ref pos));
                continue;
            }

            if (char.IsDigit(ch))
            {
                tokens.Add(ParseNumber(source, ref pos));
                continue;
            }

            if (IsIdentifierStart(ch))
            {
                var start = pos;
                pos++;
                while (pos < source.Length && IsIdentifierPart(source[pos]))
                    pos++;

                var text = source[start..pos].ToString();

                TokenType type = TokenType.Identifier;
                if (Keywords.Contains(text))
                    type = TokenType.Keyword;
                else if (BuiltInTypes.Contains(text))
                    type = TokenType.Type;
                else if (BuiltInConstants.Contains(text))
                    type = TokenType.Keyword;

                tokens.Add(new Token(type, text));
                continue;
            }

            if (IsOperatorStart(ch))
            {
                var start = pos;
                pos++;
                while (pos < source.Length && IsOperatorPart(source[pos]))
                    pos++;
                tokens.Add(new Token(TokenType.Operator, source[start..pos].ToString()));
                continue;
            }

            if (IsPunctuation(ch))
            {
                tokens.Add(new Token(TokenType.Punctuation, ch.ToString()));
                pos++;
                continue;
            }

            tokens.Add(new Token(TokenType.Text, ch.ToString()));
            pos++;
        }

        return tokens;
    }

    private static Token ParseString(ReadOnlySpan<char> source, ref int pos, char delimiter)
    {
        var start = pos;
        pos++; // Skip initial delimiter

        if (delimiter == '`')
        {
            while (pos < source.Length)
            {
                if (source[pos] == '`')
                {
                    pos++;
                    break;
                }
                pos++;
            }
        }
        else
        {
            while (pos < source.Length)
            {
                if (source[pos] == '\\' && pos + 1 < source.Length)
                {
                    pos += 2;
                    continue;
                }
                if (source[pos] == delimiter)
                {
                    pos++;
                    break;
                }
                pos++;
            }
        }

        return new Token(TokenType.String, source[start..pos].ToString());
    }

    private static Token ParseRune(ReadOnlySpan<char> source, ref int pos)
    {
        var start = pos;
        pos++; // Skip opening '
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
        return new Token(TokenType.String, source[start..pos].ToString());
    }

    private static Token ParseNumber(ReadOnlySpan<char> source, ref int pos)
    {
        var start = pos;
        var isHex = false;
        var isBinary = false;
        var isOctal = false;
        var hasExponent = false;

        if (pos + 1 < source.Length && source[pos] == '0')
        {
            var next = char.ToLowerInvariant(source[pos + 1]);
            if (next == 'x') { isHex = true; pos += 2; }
            else if (next == 'b') { isBinary = true; pos += 2; }
            else if (next == 'o') { isOctal = true; pos += 2; }
        }

        while (pos < source.Length)
        {
            var current = source[pos];

            if (isHex && IsHexDigit(current))
            {
                pos++;
                continue;
            }

            if (isBinary && current is '0' or '1')
            {
                pos++;
                continue;
            }

            if (isOctal && current >= '0' && current <= '7')
            {
                pos++;
                continue;
            }

            if (!isHex && !isBinary && !isOctal && char.IsDigit(current))
            {
                pos++;
                continue;
            }

            if (!isHex && !isBinary && !isOctal && current == '.' &&
                pos + 1 < source.Length && char.IsDigit(source[pos + 1]))
            {
                pos++;
                continue;
            }

            if (!hasExponent && !isBinary && !isOctal &&
                (current == 'e' || current == 'E' || current == 'p' || current == 'P'))
            {
                hasExponent = true;
                pos++;
                if (pos < source.Length && (source[pos] == '+' || source[pos] == '-'))
                    pos++;
                continue;
            }

            if (current == '_')
            {
                pos++;
                continue;
            }

            if (current == 'i')
            {
                pos++;
                break;
            }

            break;
        }

        return new Token(TokenType.Number, source[start..pos].ToString());
    }

    private static bool IsIdentifierStart(char ch) =>
        char.IsLetter(ch) || ch == '_';

    private static bool IsIdentifierPart(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_';

    private static bool IsOperatorStart(char ch) =>
        ch == '+' || ch == '-' || ch == '*' || ch == '/' || ch == '%' ||
        ch == '=' || ch == '!' || ch == '<' || ch == '>' || ch == '&' ||
        ch == '|' || ch == '^' || ch == '~' || ch == ':' || ch == '.';

    private static bool IsOperatorPart(char ch) =>
        ch == '+' || ch == '-' || ch == '=' || ch == '&' || ch == '|' ||
        ch == '<' || ch == '>' || ch == '!' || ch == '*' || ch == '/' ||
        ch == '%' || ch == '^' || ch == '.' || ch == ':';

    private static bool IsPunctuation(char ch) =>
        ch == '{' || ch == '}' || ch == '(' || ch == ')' || ch == '[' || ch == ']' ||
        ch == ',' || ch == ';';

    private static bool IsHexDigit(char ch) =>
        char.IsDigit(ch) || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
}
