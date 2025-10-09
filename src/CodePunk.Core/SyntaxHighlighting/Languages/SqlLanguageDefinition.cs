using CodePunk.Core.SyntaxHighlighting.Abstractions;
using CodePunk.Core.SyntaxHighlighting.Tokenization;

namespace CodePunk.Core.SyntaxHighlighting.Languages;

/// <summary>
/// SQL language definition for syntax highlighting.
/// Uses a simple tokenizer focused on T-SQL like dialects.
/// </summary>
public class SqlLanguageDefinition : ILanguageDefinition
{
    public string Name => "sql";
    public string[] Aliases => new[] { "tsql", "postgres", "mysql", "sqlite" };

    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "select", "insert", "update", "delete", "from", "where", "group",
        "by", "order", "having", "into", "values", "set", "join", "inner",
        "left", "right", "full", "outer", "on", "create", "alter", "drop",
        "table", "view", "procedure", "function", "trigger", "constraint",
        "primary", "key", "foreign", "references", "if", "exists", "not",
        "null", "and", "or", "between", "like", "in", "distinct", "as",
        "case", "when", "then", "else", "end", "union", "all", "limit",
        "offset", "top", "with", "nolock", "count", "avg", "sum", "min",
        "max", "begin", "transaction", "commit", "rollback", "declare",
        "cursor", "fetch", "open", "close", "while", "loop", "for"
    };

    private static readonly HashSet<string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        "int", "bigint", "smallint", "tinyint", "decimal", "numeric", "money",
        "float", "real", "bit", "char", "varchar", "nvarchar", "text",
        "ntext", "date", "datetime", "datetime2", "smalldatetime",
        "time", "timestamp", "uniqueidentifier", "binary", "varbinary",
        "json", "xml", "blob"
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

            if (ch == '-' && pos + 1 < source.Length && source[pos + 1] == '-')
            {
                var start = pos;
                pos += 2;
                while (pos < source.Length && source[pos] != '\n')
                    pos++;
                tokens.Add(new Token(TokenType.Comment, source.Slice(start, pos - start).ToString()));
                continue;
            }

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

            if (ch == '\'' || ch == '"')
            {
                var start = pos;
                var delimiter = ch;
                pos++;
                while (pos < source.Length)
                {
                    if (source[pos] == delimiter)
                    {
                        pos++;
                        if (pos < source.Length && source[pos] == delimiter)
                        {
                            pos++;
                            continue;
                        }
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
                var seenDecimal = false;
                while (pos < source.Length)
                {
                    var current = source[pos];
                    if (char.IsDigit(current))
                    {
                        pos++;
                        continue;
                    }
                    if (!seenDecimal && current == '.')
                    {
                        seenDecimal = true;
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
                var normalized = text.Trim('[', ']').Trim('"');

                TokenType type = TokenType.Identifier;
                if (Keywords.Contains(normalized))
                    type = TokenType.Keyword;
                else if (Types.Contains(normalized))
                    type = TokenType.Type;

                tokens.Add(new Token(type, text));
                continue;
            }

            if (IsOperator(ch))
            {
                tokens.Add(new Token(TokenType.Operator, ch.ToString()));
                pos++;
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
        char.IsLetter(ch) || ch == '_' || ch == '@' || ch == '#';

    private static bool IsIdentifierPart(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_' || ch == '@' || ch == '#' || ch == '$';

    private static bool IsOperator(char ch) =>
        ch == '+' || ch == '-' || ch == '*' || ch == '/' || ch == '%' ||
        ch == '=' || ch == '!' || ch == '<' || ch == '>' || ch == '|';

    private static bool IsPunctuation(char ch) =>
        ch == ',' || ch == ';' || ch == '(' || ch == ')' || ch == '.' || ch == '[' || ch == ']';
}
