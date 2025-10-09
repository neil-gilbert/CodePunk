using CodePunk.Core.SyntaxHighlighting.Languages;
using CodePunk.Core.SyntaxHighlighting.Tokenization;
using Xunit;

namespace CodePunk.Core.Tests.SyntaxHighlighting;

public class CSharpLanguageDefinitionTests
{
    private readonly CSharpLanguageDefinition _language = new();

    [Fact]
    public void Matches_ReturnsTrue_ForCSharpIdentifiers()
    {
        Assert.True(_language.Matches("csharp"));
        Assert.True(_language.Matches("cs"));
        Assert.True(_language.Matches("c#"));
        Assert.True(_language.Matches("CSharp"));
        Assert.True(_language.Matches("DOTNET"));
    }

    [Fact]
    public void Matches_ReturnsFalse_ForOtherLanguages()
    {
        Assert.False(_language.Matches("java"));
        Assert.False(_language.Matches("python"));
        Assert.False(_language.Matches(""));
        Assert.False(_language.Matches(null!));
    }

    [Fact]
    public void Tokenize_Keywords_AreIdentifiedCorrectly()
    {
        var code = "public class Program";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "public");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "class");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "Program");
    }

    [Fact]
    public void Tokenize_Strings_AreIdentifiedCorrectly()
    {
        var code = "var message = \"Hello World\";";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "var");
        Assert.Contains(tokens, t => t.Type == TokenType.String && t.Value == "\"Hello World\"");
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
        var code = "int x = 42;";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Type && t.Value == "int");
        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == "42");
    }

    [Fact]
    public void Tokenize_Operators_AreIdentifiedCorrectly()
    {
        var code = "x += 5;";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Operator && t.Value == "+=");
    }

    [Fact]
    public void Tokenize_PreprocessorDirectives_AreIdentifiedCorrectly()
    {
        var code = "#if DEBUG";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Preprocessor && t.Value.StartsWith("#if"));
    }

    [Fact]
    public void Tokenize_ComplexCode_ProducesCorrectTokens()
    {
        var code = @"public class Calculator
{
    public int Add(int a, int b)
    {
        return a + b; // Return sum
    }
}";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        // Verify we have various token types
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "public");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "class");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "Calculator");
        Assert.Contains(tokens, t => t.Type == TokenType.Type && t.Value == "int");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "Add");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "return");
        Assert.Contains(tokens, t => t.Type == TokenType.Operator && t.Value == "+");
        Assert.Contains(tokens, t => t.Type == TokenType.Comment && t.Value.Contains("Return sum"));
        Assert.Contains(tokens, t => t.Type == TokenType.Punctuation && t.Value == "{");
        Assert.Contains(tokens, t => t.Type == TokenType.Punctuation && t.Value == ";");
    }
}
