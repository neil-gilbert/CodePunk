using CodePunk.Core.SyntaxHighlighting.Languages;
using CodePunk.Core.SyntaxHighlighting.Tokenization;
using Xunit;

namespace CodePunk.Core.Tests.SyntaxHighlighting;

public class GoLanguageDefinitionTests
{
    private readonly GoLanguageDefinition _language = new();

    [Fact]
    public void Matches_ReturnsTrue_ForGoIdentifiers()
    {
        Assert.True(_language.Matches("go"));
        Assert.True(_language.Matches("GO"));
        Assert.True(_language.Matches("golang"));
    }

    [Fact]
    public void Matches_ReturnsFalse_ForOtherLanguages()
    {
        Assert.False(_language.Matches("python"));
        Assert.False(_language.Matches("typescript"));
        Assert.False(_language.Matches(""));
        Assert.False(_language.Matches(null!));
    }

    [Fact]
    public void Tokenize_Keywords_And_Types_AreIdentified()
    {
        var code = "package main\n\nfunc add(a int, b int) int { return a + b }";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "package");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "func");
        Assert.Contains(tokens, t => t.Type == TokenType.Type && t.Value == "int");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "return");
    }

    [Fact]
    public void Tokenize_Strings_And_Runes_AreIdentified()
    {
        var code = "msg := \"Hello\"\nraw := `line1\\nline2`\nr := 'a'";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.String && t.Value == "\"Hello\"");
        Assert.Contains(tokens, t => t.Type == TokenType.String && t.Value == "`line1\\nline2`");
        Assert.Contains(tokens, t => t.Type == TokenType.String && t.Value == "'a'");
    }

    [Fact]
    public void Tokenize_Comments_AreIdentifiedCorrectly()
    {
        var code = @"// Single line comment
        /*
           Multi line comment
        */";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Comment && t.Value.Contains("Single line"));
        Assert.Contains(tokens, t => t.Type == TokenType.Comment && t.Value.Contains("Multi line comment"));
    }

    [Fact]
    public void Tokenize_Numbers_AreIdentifiedCorrectly()
    {
        var code = "a := 42\nb := 0xFF\nc := 0b1010\nd := 3.14e+2";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == "42");
        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == "0xFF");
        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == "0b1010");
        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == "3.14e+2");
    }

    [Fact]
    public void Tokenize_Complex_Code_ProducesExpectedTokens()
    {
        var code = @"type User struct {
    ID   int
    Name string
}

func (u *User) Greeting() string {
    if u == nil {
        return ""unknown""
    }
    return ""Hello, "" + u.Name
}";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "type");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "User");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "struct");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "func");
        Assert.Contains(tokens, t => t.Type == TokenType.String && t.Value.Contains("Hello"));
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "return");
    }
}
