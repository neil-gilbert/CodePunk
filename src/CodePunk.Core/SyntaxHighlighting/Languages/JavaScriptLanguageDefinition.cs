using CodePunk.Core.SyntaxHighlighting.Abstractions;
using CodePunk.Core.SyntaxHighlighting.Tokenization;

namespace CodePunk.Core.SyntaxHighlighting.Languages;

/// <summary>
/// JavaScript language definition for syntax highlighting.
/// Provides a lightweight tokenizer tailored for JS/Node code.
/// </summary>
public class JavaScriptLanguageDefinition : ILanguageDefinition
{
    public string Name => "javascript";
    public string[] Aliases => new[] { "js", "node", "cjs", "mjs", "jsx" };

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "break", "case", "catch", "class", "const", "continue", "debugger",
        "default", "delete", "do", "else", "export", "extends", "finally",
        "for", "function", "if", "import", "in", "instanceof", "let",
        "new", "return", "super", "switch", "this", "throw", "try",
        "typeof", "var", "void", "while", "with", "yield", "async", "await",
        "static", "get", "set"
    };

    private static readonly HashSet<string> BuiltInTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "string", "number", "boolean", "object", "symbol", "bigint", "undefined",
        "Array", "Date", "RegExp", "Promise", "Map", "Set", "WeakMap", "WeakSet",
        "Function", "Error"
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
                var start = pos;
                pos++;
                while (pos < source.Length)
                {
                    if (source[pos] == '\\' && pos + 1 < source.Length)
                    {
                        pos += 2;
                        continue;
                    }
                    if (source[pos] == ch)
                    {
                        pos++;
                        break;
                    }
                    pos++;
                }
                tokens.Add(new Token(TokenType.String, source.Slice(start, pos - start).ToString()));
                continue;
            }

            if (ch == '`')
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
                    pos++;
                }
                tokens.Add(new Token(TokenType.String, source.Slice(start, pos - start).ToString()));
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

                    if (!isHex && !isBinary && !isOctal && !hasExponent &&
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

            if (IsIdentifierStart(ch))
            {
                var start = pos;
                pos++;
                while (pos < source.Length && IsIdentifierPart(source[pos]))
                    pos++;

                var text = source.Slice(start, pos - start).ToString();
                var lower = text.ToLowerInvariant();

                TokenType type = TokenType.Identifier;
                if (Keywords.Contains(lower))
                    type = TokenType.Keyword;
                else if (Literals.Contains(lower))
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
        ch == '/' || ch == '%';

    private static bool IsPunctuation(char ch) =>
        ch == '{' || ch == '}' || ch == '(' || ch == ')' || ch == '[' || ch == ']' ||
        ch == ';' || ch == ',' || ch == ':';

    private static bool IsHexDigit(char ch) =>
        char.IsDigit(ch) || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
}
