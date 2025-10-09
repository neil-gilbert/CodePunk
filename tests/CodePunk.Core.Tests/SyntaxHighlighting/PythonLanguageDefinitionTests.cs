using CodePunk.Core.SyntaxHighlighting.Languages;
using CodePunk.Core.SyntaxHighlighting.Tokenization;
using Xunit;

namespace CodePunk.Core.Tests.SyntaxHighlighting;

public class PythonLanguageDefinitionTests
{
    private readonly PythonLanguageDefinition _language = new();

    [Fact]
    public void Matches_ReturnsTrue_ForPythonIdentifiers()
    {
        Assert.True(_language.Matches("python"));
        Assert.True(_language.Matches("PYTHON"));
        Assert.True(_language.Matches("py"));
        Assert.True(_language.Matches("python3"));
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
    public void Tokenize_Keywords_And_Identifiers_AreIdentified()
    {
        var code = "def greet(name):\n    return f\"Hello {name}\"";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "def");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "greet");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "name");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "return");
        Assert.Contains(tokens, t => t.Type == TokenType.String && t.Value == "f\"Hello {name}\"");
    }

    [Fact]
    public void Tokenize_TripleQuotedStrings_AreIdentified()
    {
        var code = "\"\"\"This is a docstring\"\"\"";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Single(tokens.Where(t => t.Type == TokenType.String));
        Assert.Equal("\"\"\"This is a docstring\"\"\"", tokens.Single(t => t.Type == TokenType.String).Value);
    }

    [Fact]
    public void Tokenize_Comments_AreIdentifiedCorrectly()
    {
        var code = "# This is a comment\nx = 1";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Comment && t.Value.StartsWith("#"));
    }

    [Fact]
    public void Tokenize_Numbers_AreIdentifiedCorrectly()
    {
        var code = "value = 0xFF + 3.14e2";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == "0xFF");
        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == "3.14e2");
    }

    [Fact]
    public void Tokenize_BuiltInTypes_AreIdentifiedCorrectly()
    {
        var code = "items: list[int] = []";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Type && t.Value == "list");
    }

    [Fact]
    public void Tokenize_Complex_Code_ProducesExpectedTokens()
    {
        var code = @"import asyncio

async def fetch(session, url):
    async with session.get(url) as response:
        if response.status != 200:
            raise ValueError(f""Failed: {response.status}"")
        return await response.text()";

        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "import");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "asyncio");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "async");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "fetch");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "response");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "raise");
        Assert.Contains(tokens, t => t.Type == TokenType.String && t.Value.Contains("{response.status}"));
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "await");
    }
}
