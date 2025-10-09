using CodePunk.Core.SyntaxHighlighting.Languages;
using CodePunk.Core.SyntaxHighlighting.Tokenization;
using Xunit;

namespace CodePunk.Core.Tests.SyntaxHighlighting;

public class TypeScriptLanguageDefinitionTests
{
    private readonly TypeScriptLanguageDefinition _language = new();

    [Fact]
    public void Matches_ReturnsTrue_ForTypeScriptIdentifiers()
    {
        Assert.True(_language.Matches("typescript"));
        Assert.True(_language.Matches("ts"));
        Assert.True(_language.Matches("TSX"));
        Assert.True(_language.Matches("mts"));
        Assert.True(_language.Matches("cts"));
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
    public void Tokenize_Keywords_And_Types_AreIdentified()
    {
        var code = "type User = { id: number; name: string };";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "type");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "User");
        Assert.Contains(tokens, t => t.Type == TokenType.Type && t.Value == "number");
        Assert.Contains(tokens, t => t.Type == TokenType.Type && t.Value == "string");
    }

    [Fact]
    public void Tokenize_Decorators_AreIdentified()
    {
        var code = "@Injectable()\nclass Service {}";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "@Injectable");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "class");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "Service");
    }

    [Fact]
    public void Tokenize_String_Templates_AreIdentified()
    {
        var code = "const message = `Hello ${user.name}`;";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "const");
        Assert.Contains(tokens, t => t.Type == TokenType.String && t.Value == "`Hello ${user.name}`");
    }

    [Fact]
    public void Tokenize_Comments_AreIdentifiedCorrectly()
    {
        var code = @"// Single line comment
        /* Multi line comment */";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Comment && t.Value.Contains("Single line"));
        Assert.Contains(tokens, t => t.Type == TokenType.Comment && t.Value.Contains("Multi line"));
    }

    [Fact]
    public void Tokenize_Numbers_AreIdentifiedCorrectly()
    {
        var code = "const value = 0xFF + 1_000;";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == "0xFF");
        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == "1_000");
    }

    [Fact]
    public void Tokenize_Generic_Functions_AreIdentified()
    {
        var code = "function identity<T>(value: T): T { return value; }";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "function");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "identity");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "T");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "return");
    }
}
