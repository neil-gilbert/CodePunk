using CodePunk.Core.SyntaxHighlighting.Languages;
using CodePunk.Core.SyntaxHighlighting.Tokenization;
using Xunit;

namespace CodePunk.Core.Tests.SyntaxHighlighting;

public class SqlLanguageDefinitionTests
{
    private readonly SqlLanguageDefinition _language = new();

    [Fact]
    public void Matches_ReturnsTrue_ForSqlIdentifiers()
    {
        Assert.True(_language.Matches("sql"));
        Assert.True(_language.Matches("SQL"));
        Assert.True(_language.Matches("tsql"));
        Assert.True(_language.Matches("postgres"));
        Assert.True(_language.Matches("mysql"));
    }

    [Fact]
    public void Matches_ReturnsFalse_ForOtherLanguages()
    {
        Assert.False(_language.Matches("csharp"));
        Assert.False(_language.Matches("javascript"));
        Assert.False(_language.Matches(""));
        Assert.False(_language.Matches(null!));
    }

    [Fact]
    public void Tokenize_Keywords_AreIdentifiedCorrectly()
    {
        var sql = "SELECT * FROM Users WHERE Id = 1;";
        var tokens = _language.Tokenize(sql.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && string.Equals(t.Value, "SELECT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && string.Equals(t.Value, "FROM", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && string.Equals(t.Value, "Users", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && string.Equals(t.Value, "WHERE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Tokenize_Strings_AreIdentifiedCorrectly()
    {
        var sql = "SELECT 'O''Reilly' AS Name;";
        var tokens = _language.Tokenize(sql.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.String && t.Value == "'O''Reilly'");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && string.Equals(t.Value, "SELECT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Tokenize_Comments_AreIdentifiedCorrectly()
    {
        var sql = @"-- Single line comment
        SELECT 1; /* Multi
        line comment */";
        var tokens = _language.Tokenize(sql.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Comment && t.Value.Contains("Single line"));
        Assert.Contains(tokens, t => t.Type == TokenType.Comment && t.Value.Contains("Multi"));
    }

    [Fact]
    public void Tokenize_Numbers_AreIdentifiedCorrectly()
    {
        var sql = "INSERT INTO Orders (Amount) VALUES (123.45);";
        var tokens = _language.Tokenize(sql.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == "123.45");
    }

    [Fact]
    public void Tokenize_Types_AreIdentifiedCorrectly()
    {
        var sql = "CREATE TABLE Customers (Id INT, Name NVARCHAR(100));";
        var tokens = _language.Tokenize(sql.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Type && string.Equals(t.Value, "INT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, t => t.Type == TokenType.Type && string.Equals(t.Value, "NVARCHAR", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Tokenize_ComplexQuery_ProducesExpectedTokens()
    {
        var sql = @"WITH LatestOrders AS (
    SELECT o.CustomerId, MAX(o.OrderDate) AS LastOrder
    FROM Orders o
    GROUP BY o.CustomerId
)
SELECT c.Name, lo.LastOrder
FROM Customers c
JOIN LatestOrders lo ON lo.CustomerId = c.Id
WHERE lo.LastOrder >= DATEADD(day, -30, GETDATE());";

        var tokens = _language.Tokenize(sql.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && string.Equals(t.Value, "WITH", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && string.Equals(t.Value, "LatestOrders", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && string.Equals(t.Value, "JOIN", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, t => t.Type == TokenType.Operator && t.Value == "=");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && string.Equals(t.Value, "DATEADD", StringComparison.OrdinalIgnoreCase));
    }
}
