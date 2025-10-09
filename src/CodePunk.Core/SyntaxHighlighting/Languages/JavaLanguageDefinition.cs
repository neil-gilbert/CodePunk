using CodePunk.Core.SyntaxHighlighting.Abstractions;
using CodePunk.Core.SyntaxHighlighting.Tokenization;

namespace CodePunk.Core.SyntaxHighlighting.Languages;

/// <summary>
/// Java language definition for syntax highlighting.
/// Provides a tokenizer that recognizes keywords, literals, and annotations.
/// </summary>
public class JavaLanguageDefinition : ILanguageDefinition
{
    public string Name => "java";
    public string[] Aliases => new[] { "jdk", "jsp" };

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "assert", "boolean", "break", "byte", "case", "catch", "char",
        "class", "const", "continue", "default", "do", "double", "else", "enum",
        "exports", "extends", "final", "finally", "float", "for", "goto", "if",
        "implements", "import", "instanceof", "int", "interface", "long", "module",
        "native", "new", "package", "private", "protected", "public", "return",
        "short", "static", "strictfp", "super", "switch", "synchronized", "this",
        "throw", "throws", "transient", "try", "void", "volatile", "while",
        "record", "sealed", "non-sealed", "permits", "var", "yield"
    };

    private static readonly HashSet<string> BuiltInTypes = new(StringComparer.Ordinal)
    {
        "int", "long", "float", "double", "boolean", "char", "byte", "short",
        "void", "String", "Object"
    };

    private static readonly HashSet<string> Literals = new(StringComparer.Ordinal)
    {
        "true", "false", "null"
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

            if (ch == '"')
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

            if (char.IsDigit(ch))
            {
                var start = pos;
                var hasExponent = false;
                var isHex = false;
                var isBinary = false;
                var isOctal = false;

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
                        (current == 'e' || current == 'E'))
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

                    if (current is 'f' or 'F' or 'd' or 'D' or 'l' or 'L')
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
                if (BuiltInTypes.Contains(text) || BuiltInTypes.Contains(lower))
                    type = TokenType.Type;
                else if (Literals.Contains(lower))
                    type = TokenType.Keyword;
                else if (Keywords.Contains(lower))
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
        ch == '|' || ch == '^' || ch == '~' || ch == '?' || ch == ':';

    private static bool IsOperatorPart(char ch) =>
        ch == '+' || ch == '-' || ch == '=' || ch == '&' || ch == '|' ||
        ch == '<' || ch == '>' || ch == '?' || ch == '!' || ch == ':' ||
        ch == '^' || ch == '*';

    private static bool IsPunctuation(char ch) =>
        ch == '{' || ch == '}' || ch == '(' || ch == ')' || ch == '[' || ch == ']' ||
        ch == ';' || ch == ',' || ch == '.';

    private static bool IsHexDigit(char ch) =>
        char.IsDigit(ch) || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
}
