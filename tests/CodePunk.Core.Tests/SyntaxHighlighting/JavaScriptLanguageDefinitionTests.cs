using CodePunk.Core.SyntaxHighlighting.Languages;
using CodePunk.Core.SyntaxHighlighting.Tokenization;
using Xunit;

namespace CodePunk.Core.Tests.SyntaxHighlighting;

public class JavaScriptLanguageDefinitionTests
{
    private readonly JavaScriptLanguageDefinition _language = new();

    [Fact]
    public void Matches_ReturnsTrue_ForJavaScriptIdentifiers()
    {
        Assert.True(_language.Matches("javascript"));
        Assert.True(_language.Matches("js"));
        Assert.True(_language.Matches("node"));
        Assert.True(_language.Matches("JAVASCRIPT"));
        Assert.True(_language.Matches("Js"));
    }

    [Fact]
    public void Matches_ReturnsFalse_ForOtherLanguages()
    {
        Assert.False(_language.Matches("csharp"));
        Assert.False(_language.Matches("python"));
        Assert.False(_language.Matches(""));
        Assert.False(_language.Matches(null!));
    }

    [Fact]
    public void Tokenize_Keywords_AreIdentifiedCorrectly()
    {
        var code = "const result = await fetch(url);";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "const");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "await");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "result");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "fetch");
    }

    [Fact]
    public void Tokenize_Strings_AreIdentifiedCorrectly()
    {
        var code = "const greeting = `Hello ${name}`;";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "const");
        Assert.Contains(tokens, t => t.Type == TokenType.String && t.Value == "`Hello ${name}`");
    }

    [Fact]
    public void Tokenize_Comments_AreIdentifiedCorrectly()
    {
        var code = @"// Single line comment
        /* Multi line
           comment */";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Comment && t.Value.Contains("Single line"));
        Assert.Contains(tokens, t => t.Type == TokenType.Comment && t.Value.Contains("Multi line"));
    }

    [Fact]
    public void Tokenize_Numbers_AreIdentifiedCorrectly()
    {
        var code = "const value = 0xFF + 10n;";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "const");
        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == "0xFF");
        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == "10n");
    }

    [Fact]
    public void Tokenize_Literals_AreIdentifiedAsKeywords()
    {
        var code = "if (value === null || value === undefined) return true;";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "if");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "null");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "undefined");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "return");
    }

    [Fact]
    public void Tokenize_Operators_And_Punctuation_AreIdentified()
    {
        var code = "const total = items.reduce((sum, item) => sum + item.price, 0);";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Operator && t.Value == "=");
        Assert.Contains(tokens, t => t.Type == TokenType.Operator && t.Value == "=>");
        Assert.Contains(tokens, t => t.Type == TokenType.Operator && t.Value == "+");
        Assert.Contains(tokens, t => t.Type == TokenType.Punctuation && t.Value == "(");
        Assert.Contains(tokens, t => t.Type == TokenType.Punctuation && t.Value == ")");
    }

    [Fact]
    public void Tokenize_ComplexCode_ProducesExpectedTokens()
    {
        var code = @"export class ApiClient {
    constructor(baseUrl) {
        this.baseUrl = baseUrl;
    }

    async get(path) {
        const response = await fetch(`${this.baseUrl}${path}`);
        if (!response.ok) {
            throw new Error(`Request failed: ${response.status}`);
        }
        return await response.json();
    }
}";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "export");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "class");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "ApiClient");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "constructor");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "async");
        Assert.Contains(tokens, t => t.Type == TokenType.String && t.Value.Contains("${this.baseUrl}"));
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "fetch");
        Assert.Contains(tokens, t => t.Type == TokenType.Type && t.Value == "Error");
    }
}
