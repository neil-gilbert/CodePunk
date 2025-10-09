using CodePunk.Core.SyntaxHighlighting.Languages;
using CodePunk.Core.SyntaxHighlighting.Tokenization;
using Xunit;

namespace CodePunk.Core.Tests.SyntaxHighlighting;

public class JavaLanguageDefinitionTests
{
    private readonly JavaLanguageDefinition _language = new();

    [Fact]
    public void Matches_ReturnsTrue_ForJavaIdentifiers()
    {
        Assert.True(_language.Matches("java"));
        Assert.True(_language.Matches("JAVA"));
        Assert.True(_language.Matches("jsp"));
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
        var code = "public class User { private int id; }";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "public");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "class");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "User");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "private");
        Assert.Contains(tokens, t => t.Type == TokenType.Type && t.Value == "int");
    }

    [Fact]
    public void Tokenize_Strings_And_Characters_AreIdentified()
    {
        var code = "String greeting = \"Hello\"; char initial = 'A';";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Type && t.Value == "String");
        Assert.Contains(tokens, t => t.Type == TokenType.String && t.Value == "\"Hello\"");
        Assert.Contains(tokens, t => t.Type == TokenType.String && t.Value == "'A'");
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
        var code = "double value = 3.14e10; long mask = 0xFFL;";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == "3.14e10");
        Assert.Contains(tokens, t => t.Type == TokenType.Number && t.Value == "0xFFL");
    }

    [Fact]
    public void Tokenize_Annotations_AreIdentified()
    {
        var code = "@Override public String toString() { return \"\"; }";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "@Override");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "public");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "toString");
    }

    [Fact]
    public void Tokenize_Complex_Code_ProducesExpectedTokens()
    {
        var code = @"public record Customer(String id, String name) {
    private static final Logger logger = LoggerFactory.getLogger(Customer.class);

    public String formattedName() {
        if (name == null || name.isBlank()) {
            throw new IllegalArgumentException(""Name required"");
        }
        return name.trim();
    }
}";
        var tokens = _language.Tokenize(code.AsSpan()).ToList();

        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "public");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "record");
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Value == "Customer");
        Assert.Contains(tokens, t => t.Type == TokenType.Type && t.Value == "String");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "private");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "static");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "final");
        Assert.Contains(tokens, t => t.Type == TokenType.Keyword && t.Value == "throw");
        Assert.Contains(tokens, t => t.Type == TokenType.String && t.Value.Contains("Name required"));
    }
}
