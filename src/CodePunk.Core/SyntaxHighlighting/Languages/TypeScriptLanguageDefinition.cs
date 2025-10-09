using CodePunk.Core.SyntaxHighlighting.Abstractions;
using CodePunk.Core.SyntaxHighlighting.Tokenization;

namespace CodePunk.Core.SyntaxHighlighting.Languages;

/// <summary>
/// TypeScript language definition for syntax highlighting.
/// Extends JavaScript tokenization with type keywords and decorators.
/// </summary>
public class TypeScriptLanguageDefinition : ILanguageDefinition
{
    public string Name => "typescript";
    public string[] Aliases => new[] { "ts", "tsx", "cts", "mts" };

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "any", "as", "asserts", "async", "await", "break", "case",
        "catch", "class", "const", "constructor", "continue", "declare", "default",
        "delete", "do", "else", "enum", "export", "extends", "finally", "for",
        "from", "function", "get", "if", "implements", "import", "in", "infer",
        "instanceof", "interface", "is", "keyof", "let", "module", "namespace",
        "never", "new", "of", "package", "private", "protected", "public",
        "readonly", "require", "return", "satisfies", "set", "static", "super",
        "switch", "this", "throw", "try", "type", "typeof", "unique", "var",
        "void", "while", "with", "yield"
    };

    private static readonly HashSet<string> BuiltInTypes = new(StringComparer.Ordinal)
    {
        "string", "number", "boolean", "symbol", "bigint", "unknown", "any",
        "never", "void", "undefined", "null", "object", "Record", "Array",
        "Promise", "Map", "Set", "WeakMap", "WeakSet", "Readonly", "Partial",
        "Required", "Pick", "Omit"
    };

    private static readonly HashSet<string> Literals = new(StringComparer.Ordinal)
    {
        "true", "false", "null", "undefined", "NaN", "Infinity"
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
                tokens.Add(new Token(TokenType.Text, source.Slice(start, pos - start).ToString()));
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
                    tokens.Add(new Token(TokenType.Comment, source.Slice(start, pos - start).ToString()));
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
                    tokens.Add(new Token(TokenType.Comment, source.Slice(start, pos - start).ToString()));
                    continue;
                }
            }

            if (ch == '"' || ch == '\'')
            {
                tokens.Add(ParseString(source, ref pos, ch));
                continue;
            }

            if (ch == '`')
            {
                tokens.Add(ParseTemplateString(source, ref pos));
                continue;
            }

            if (char.IsDigit(ch))
            {
                var start = pos;
                var hasExponent = false;
                var isHex = false;
                var isBinary = false;
                var isOctal = false;

                while (pos < source.Length)
                {
                    var current = source[pos];

                    if (char.IsDigit(current))
                    {
                        if (isBinary && current is not ('0' or '1'))
                            break;
                        if (isOctal && current is < '0' or > '7')
                            break;
                        pos++;
                        continue;
                    }

                    if (!isHex && !isBinary && !isOctal && current == '.' &&
                        pos + 1 < source.Length && char.IsDigit(source[pos + 1]))
                    {
                        pos++;
                        continue;
                    }

                    if (current == '_')
                    {
                        pos++;
                        continue;
                    }

                    if (!hasExponent && !isHex && !isBinary && !isOctal &&
                        (current == 'e' || current == 'E'))
                    {
                        hasExponent = true;
                        pos++;
                        if (pos < source.Length && (source[pos] == '+' || source[pos] == '-'))
                            pos++;
                        continue;
                    }

                    if (!isHex && !isBinary && !isOctal && (current == 'x' || current == 'X'))
                    {
                        isHex = true;
                        pos++;
                        continue;
                    }

                    if (!isHex && !isBinary && !isOctal && (current == 'b' || current == 'B'))
                    {
                        isBinary = true;
                        pos++;
                        continue;
                    }

                    if (!isHex && !isBinary && !isOctal && (current == 'o' || current == 'O'))
                    {
                        isOctal = true;
                        pos++;
                        continue;
                    }

                    if (isHex && IsHexDigit(current))
                    {
                        pos++;
                        continue;
                    }

                    if (current == 'n')
                    {
                        pos++;
                        break;
                    }

                    break;
                }

                tokens.Add(new Token(TokenType.Number, source.Slice(start, pos - start).ToString()));
                continue;
            }

            if (ch == '@' && pos + 1 < source.Length && IsIdentifierStart(source[pos + 1]))
            {
                var start = pos;
                pos += 2;
                while (pos < source.Length && IsIdentifierPart(source[pos]))
                    pos++;
                tokens.Add(new Token(TokenType.Identifier, source.Slice(start, pos - start).ToString()));
                continue;
            }

            if (IsIdentifierStart(ch))
            {
                var start = pos;
                pos++;
                while (pos < source.Length && IsIdentifierPart(source[pos]))
                    pos++;

                var text = source.Slice(start, pos - start).ToString();
                var lower = text.ToLowerInvariant();

                TokenType type = TokenType.Identifier;
                if (Keywords.Contains(text) || Keywords.Contains(lower))
                    type = TokenType.Keyword;
                else if (Literals.Contains(text) || Literals.Contains(lower))
                    type = TokenType.Keyword;
                else if (BuiltInTypes.Contains(text) || BuiltInTypes.Contains(lower))
                    type = TokenType.Type;

                tokens.Add(new Token(type, text));
                continue;
            }

            if (IsOperatorStart(ch))
            {
                var start = pos;
                pos++;
                while (pos < source.Length && IsOperatorPart(source[pos]))
                    pos++;
                tokens.Add(new Token(TokenType.Operator, source.Slice(start, pos - start).ToString()));
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
        pos++;
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
        return new Token(TokenType.String, source.Slice(start, pos - start).ToString());
    }

    private static Token ParseTemplateString(ReadOnlySpan<char> source, ref int pos)
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
            if (source[pos] == '`')
            {
                pos++;
                break;
            }
            if (source[pos] == '{')
            {
                pos++;
                while (pos < source.Length && source[pos] != '}')
                    pos++;
                if (pos < source.Length)
                    pos++;
                continue;
            }
            pos++;
        }
        return new Token(TokenType.String, source.Slice(start, pos - start).ToString());
    }

    private static bool IsIdentifierStart(char ch) =>
        char.IsLetter(ch) || ch == '_' || ch == '$';

    private static bool IsIdentifierPart(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_' || ch == '$';

    private static bool IsOperatorStart(char ch) =>
        ch == '+' || ch == '-' || ch == '*' || ch == '/' || ch == '%' ||
        ch == '=' || ch == '!' || ch == '<' || ch == '>' || ch == '&' ||
        ch == '|' || ch == '^' || ch == '~' || ch == '?' || ch == ':' ||
        ch == '.';

    private static bool IsOperatorPart(char ch) =>
        ch == '+' || ch == '-' || ch == '=' || ch == '&' || ch == '|' ||
        ch == '<' || ch == '>' || ch == '?' || ch == '!' || ch == '*' ||
        ch == '/' || ch == '%' || ch == '^' || ch == '.';

    private static bool IsPunctuation(char ch) =>
        ch == '{' || ch == '}' || ch == '(' || ch == ')' || ch == '[' || ch == ']' ||
        ch == ';' || ch == ',' || ch == '<' || ch == '>' || ch == '@';

    private static bool IsHexDigit(char ch) =>
        char.IsDigit(ch) || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
}
