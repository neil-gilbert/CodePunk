using System.Text.Json;
using CodePunk.ComponentTests.TestHelpers;
using CodePunk.Core.Tools;
using FluentAssertions;
using Xunit;

namespace CodePunk.ComponentTests;

[Collection("Sequential")]
public class SearchFilesToolTests : WorkspaceTestBase
{

    public SearchFilesToolTests() : base("search_files")
    {
    }

    [Fact]
    public async Task SearchFiles_SimplePattern_FindsMatches()
    {
        var tool = new SearchFilesTool();
        await File.WriteAllTextAsync(Path.Combine(TestWorkspace, "file1.txt"), "Hello world\nGoodbye world");
        await File.WriteAllTextAsync(Path.Combine(TestWorkspace, "file2.txt"), "No match here");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""world"",
            ""path"": ""{TestWorkspace.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("Found 2 match(es)");
        result.Content.Should().Contain("file1.txt");
        result.Content.Should().Contain("L1: Hello world");
        result.Content.Should().Contain("L2: Goodbye world");
    }

    [Fact]
    public async Task SearchFiles_RegexPattern_MatchesCorrectly()
    {
        var tool = new SearchFilesTool();
        await File.WriteAllTextAsync(Path.Combine(TestWorkspace, "code.cs"),
            "public void MyMethod()\nprivate int MyProperty\npublic class MyClass");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""public\\s+(void|class)"",
            ""path"": ""{TestWorkspace.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("Found 2 match(es)");
        result.Content.Should().Contain("MyMethod");
        result.Content.Should().Contain("MyClass");
        result.Content.Should().NotContain("MyProperty");
    }

    [Fact]
    public async Task SearchFiles_WithIncludePattern_FiltersFiles()
    {
        var tool = new SearchFilesTool();
        await File.WriteAllTextAsync(Path.Combine(TestWorkspace, "match.txt"), "findme");
        await File.WriteAllTextAsync(Path.Combine(TestWorkspace, "match.log"), "findme");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""findme"",
            ""path"": ""{TestWorkspace.Replace("\\", "\\\\")}"",
            ""include"": ""*.txt""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("Found 1 match(es)");
        result.Content.Should().Contain("match.txt");
        result.Content.Should().NotContain("match.log");
    }

    [Fact]
    public async Task SearchFiles_RecursiveIncludePattern_SearchesSubdirectories()
    {
        var tool = new SearchFilesTool();
        var subdir = Path.Combine(TestWorkspace, "src");
        Directory.CreateDirectory(subdir);
        var nested = Path.Combine(subdir, "nested");
        Directory.CreateDirectory(nested);

        await File.WriteAllTextAsync(Path.Combine(TestWorkspace, "root.cs"), "TODO: fix this");
        await File.WriteAllTextAsync(Path.Combine(subdir, "file1.cs"), "TODO: implement");
        await File.WriteAllTextAsync(Path.Combine(nested, "file2.cs"), "TODO: review");
        await File.WriteAllTextAsync(Path.Combine(nested, "other.txt"), "TODO: ignore");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""TODO:"",
            ""path"": ""{TestWorkspace.Replace("\\", "\\\\")}"",
            ""include"": ""**/*.cs""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("Found 3 match(es)");
        result.Content.Should().Contain("root.cs");
        result.Content.Should().Contain("file1.cs");
        result.Content.Should().Contain("file2.cs");
        result.Content.Should().NotContain("other.txt");
    }

    [Fact]
    public async Task SearchFiles_CaseSensitive_RespectsCase()
    {
        var tool = new SearchFilesTool();
        await File.WriteAllTextAsync(Path.Combine(TestWorkspace, "mixed.txt"), "Hello\nhello\nHELLO");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""hello"",
            ""path"": ""{TestWorkspace.Replace("\\", "\\\\")}"",
            ""case_sensitive"": true
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("Found 1 match(es)");
        result.Content.Should().Contain("L2: hello");
        result.Content.Should().NotContain("Hello");
        result.Content.Should().NotContain("HELLO");
    }

    [Fact]
    public async Task SearchFiles_CaseInsensitive_MatchesAllCases()
    {
        var tool = new SearchFilesTool();
        await File.WriteAllTextAsync(Path.Combine(TestWorkspace, "mixed.txt"), "Hello\nhello\nHELLO");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""hello"",
            ""path"": ""{TestWorkspace.Replace("\\", "\\\\")}"",
            ""case_sensitive"": false
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("Found 3 match(es)");
    }

    [Fact]
    public async Task SearchFiles_NoMatches_ReturnsEmptyResult()
    {
        var tool = new SearchFilesTool();
        await File.WriteAllTextAsync(Path.Combine(TestWorkspace, "file.txt"), "content");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""nomatch"",
            ""path"": ""{TestWorkspace.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("Found 0 match(es)");
        result.Content.Should().Contain("No matches found");
    }

    [Fact]
    public async Task SearchFiles_MultipleMatchesInSameLine_CountsOnce()
    {
        var tool = new SearchFilesTool();
        await File.WriteAllTextAsync(Path.Combine(TestWorkspace, "file.txt"), "test test test");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""test"",
            ""path"": ""{TestWorkspace.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("Found 1 match(es)");
    }

    [Fact]
    public async Task SearchFiles_ShowsLineNumbers()
    {
        var tool = new SearchFilesTool();
        await File.WriteAllTextAsync(Path.Combine(TestWorkspace, "numbered.txt"),
            "line 1\nline 2 match\nline 3\nline 4 match");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""match"",
            ""path"": ""{TestWorkspace.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("L2:");
        result.Content.Should().Contain("L4:");
    }

    [Fact]
    public async Task SearchFiles_InvalidRegex_ReturnsError()
    {
        var tool = new SearchFilesTool();

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""[invalid"",
            ""path"": ""{TestWorkspace.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeTrue();
        result.ErrorMessage.Should().Contain("Invalid regex pattern");
    }

    [Fact]
    public async Task SearchFiles_NonExistentDirectory_ReturnsError()
    {
        var tool = new SearchFilesTool();
        var nonExistent = Path.Combine(TestWorkspace, "doesnotexist");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""test"",
            ""path"": ""{nonExistent.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeTrue();
        result.ErrorMessage.Should().Contain("does not exist");
    }

    [Fact]
    public async Task SearchFiles_EmptyPattern_ReturnsError()
    {
        var tool = new SearchFilesTool();

        var arguments = JsonDocument.Parse(@"{
            ""pattern"": """"
        }").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeTrue();
        result.ErrorMessage.Should().Contain("cannot be empty");
    }
}
