using System.Text.Json;
using CodePunk.Core.Tools;
using FluentAssertions;
using Xunit;

namespace CodePunk.ComponentTests;

[Collection("Sequential")]
public class SearchFilesToolTests : IDisposable
{
    private readonly string _testWorkspace;

    public SearchFilesToolTests()
    {
        _testWorkspace = Path.Combine(Path.GetTempPath(), $"codepunk_search_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testWorkspace);
        Environment.CurrentDirectory = _testWorkspace;
    }

    [Fact]
    public async Task SearchFiles_SimplePattern_FindsMatches()
    {
        var tool = new SearchFilesTool();
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "file1.txt"), "Hello world\nGoodbye world");
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "file2.txt"), "No match here");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""world"",
            ""path"": ""{_testWorkspace.Replace("\\", "\\\\")}""
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
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "code.cs"),
            "public void MyMethod()\nprivate int MyProperty\npublic class MyClass");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""public\\s+(void|class)"",
            ""path"": ""{_testWorkspace.Replace("\\", "\\\\")}""
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
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "match.txt"), "findme");
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "match.log"), "findme");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""findme"",
            ""path"": ""{_testWorkspace.Replace("\\", "\\\\")}"",
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
        var subdir = Path.Combine(_testWorkspace, "src");
        Directory.CreateDirectory(subdir);
        var nested = Path.Combine(subdir, "nested");
        Directory.CreateDirectory(nested);

        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "root.cs"), "TODO: fix this");
        await File.WriteAllTextAsync(Path.Combine(subdir, "file1.cs"), "TODO: implement");
        await File.WriteAllTextAsync(Path.Combine(nested, "file2.cs"), "TODO: review");
        await File.WriteAllTextAsync(Path.Combine(nested, "other.txt"), "TODO: ignore");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""TODO:"",
            ""path"": ""{_testWorkspace.Replace("\\", "\\\\")}"",
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
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "mixed.txt"), "Hello\nhello\nHELLO");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""hello"",
            ""path"": ""{_testWorkspace.Replace("\\", "\\\\")}"",
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
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "mixed.txt"), "Hello\nhello\nHELLO");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""hello"",
            ""path"": ""{_testWorkspace.Replace("\\", "\\\\")}"",
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
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "file.txt"), "content");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""nomatch"",
            ""path"": ""{_testWorkspace.Replace("\\", "\\\\")}""
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
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "file.txt"), "test test test");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""test"",
            ""path"": ""{_testWorkspace.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("Found 1 match(es)");
    }

    [Fact]
    public async Task SearchFiles_ShowsLineNumbers()
    {
        var tool = new SearchFilesTool();
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "numbered.txt"),
            "line 1\nline 2 match\nline 3\nline 4 match");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""match"",
            ""path"": ""{_testWorkspace.Replace("\\", "\\\\")}""
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
            ""path"": ""{_testWorkspace.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeTrue();
        result.ErrorMessage.Should().Contain("Invalid regex pattern");
    }

    [Fact]
    public async Task SearchFiles_NonExistentDirectory_ReturnsError()
    {
        var tool = new SearchFilesTool();
        var nonExistent = Path.Combine(_testWorkspace, "doesnotexist");

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

    public void Dispose()
    {
        if (Directory.Exists(_testWorkspace))
        {
            Directory.Delete(_testWorkspace, recursive: true);
        }
    }
}
