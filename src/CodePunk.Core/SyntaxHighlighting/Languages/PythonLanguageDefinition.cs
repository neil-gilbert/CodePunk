using CodePunk.Core.SyntaxHighlighting.Abstractions;
using CodePunk.Core.SyntaxHighlighting.Tokenization;

namespace CodePunk.Core.SyntaxHighlighting.Languages;

/// <summary>
/// Python language definition for syntax highlighting.
/// Provides a lightweight tokenizer aware of comments, strings, and common operators.
/// </summary>
public class PythonLanguageDefinition : ILanguageDefinition
{
    public string Name => "python";
    public string[] Aliases => new[] { "py", "python3" };

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "and", "as", "assert", "async", "await", "break", "class", "continue",
        "def", "del", "elif", "else", "except", "False", "finally", "for",
        "from", "global", "if", "import", "in", "is", "lambda", "None",
        "nonlocal", "not", "or", "pass", "raise", "return", "True", "try",
        "while", "with", "yield"
    };

    private static readonly HashSet<string> BuiltInTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "int", "float", "bool", "str", "bytes", "list", "tuple", "set",
        "dict", "complex", "range", "object", "type"
    };

    private static readonly HashSet<char> StringPrefixChars = new(new[] { 'r', 'u', 'f', 'b', 'R', 'U', 'F', 'B' });

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

            if (ch == '#')
            {
                var start = pos;
                pos++;
                while (pos < source.Length && source[pos] != '\n')
                    pos++;
                tokens.Add(new Token(TokenType.Comment, source.Slice(start, pos - start).ToString()));
                continue;
            }

            if (IsStringPrefix(source, pos, out var prefixLength))
            {
                tokens.Add(ParseString(source, ref pos, prefixLength));
                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                tokens.Add(ParseString(source, ref pos, 0));
                continue;
            }

            if (char.IsDigit(ch))
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

                    if (isBinary && (current == '0' || current == '1'))
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

                    if (!isHex && !isBinary && !isOctal && current == '.' && pos + 1 < source.Length && char.IsDigit(source[pos + 1]))
                    {
                        pos++;
                        continue;
                    }

                    if (!hasExponent && !isHex && !isBinary && !isOctal && (current == 'e' || current == 'E'))
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
                var normalized = text.TrimStart('@');
                var lower = normalized.ToLowerInvariant();

                TokenType type = TokenType.Identifier;
                if (Keywords.Contains(normalized) || Keywords.Contains(lower))
                    type = TokenType.Keyword;
                else if (BuiltInTypes.Contains(normalized) || BuiltInTypes.Contains(lower))
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

    private static bool IsStringPrefix(ReadOnlySpan<char> source, int position, out int prefixLength)
    {
        prefixLength = 0;
        var idx = position;

        while (idx < source.Length && StringPrefixChars.Contains(source[idx]))
        {
            idx++;
        }

        if (idx == position)
            return false;

        if (idx >= source.Length)
            return false;

        var ch = source[idx];
        if (ch == '\'' || ch == '"')
        {
            prefixLength = idx - position;
            return true;
        }

        return false;
    }

    private static Token ParseString(ReadOnlySpan<char> source, ref int position, int prefixLength)
    {
        var start = position;
        position += prefixLength;

        var quote = source[position];
        var isTriple = position + 2 < source.Length &&
                       source[position + 1] == quote &&
                       source[position + 2] == quote;

        position += isTriple ? 3 : 1;

        while (position < source.Length)
        {
            if (isTriple)
            {
                if (position + 2 < source.Length &&
                    source[position] == quote &&
                    source[position + 1] == quote &&
                    source[position + 2] == quote)
                {
                    position += 3;
                    break;
                }

                position++;
                continue;
            }

            if (source[position] == '\\' && position + 1 < source.Length)
            {
                position += 2;
                continue;
            }

            if (source[position] == quote)
            {
                position++;
                break;
            }

            position++;
        }

        return new Token(TokenType.String, source.Slice(start, position - start).ToString());
    }

    private static bool IsIdentifierStart(char ch) =>
        char.IsLetter(ch) || ch == '_';

    private static bool IsIdentifierPart(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_';

    private static bool IsOperatorStart(char ch) =>
        ch == '+' || ch == '-' || ch == '*' || ch == '/' || ch == '%' ||
        ch == '=' || ch == '!' || ch == '<' || ch == '>' || ch == '&' ||
        ch == '|' || ch == '^' || ch == '~';

    private static bool IsOperatorPart(char ch) =>
        ch == '+' || ch == '-' || ch == '=' || ch == '&' || ch == '|' ||
        ch == '<' || ch == '>' || ch == '!' || ch == '*' || ch == '/' ||
        ch == '%' || ch == '^' || ch == '~' || ch == ':';

    private static bool IsPunctuation(char ch) =>
        ch == '(' || ch == ')' || ch == '{' || ch == '}' || ch == '[' || ch == ']' ||
        ch == ',' || ch == ';' || ch == '.' || ch == ':';

    private static bool IsHexDigit(char ch) =>
        char.IsDigit(ch) || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
}
