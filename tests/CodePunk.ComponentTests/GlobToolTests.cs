using System.Text.Json;
using CodePunk.Core.Tools;
using FluentAssertions;
using Xunit;

namespace CodePunk.ComponentTests;

[Collection("Sequential")]
public class GlobToolTests : IDisposable
{
    private readonly string _testWorkspace;
    private readonly string _originalDirectory;

    public GlobToolTests()
    {
        _originalDirectory = Environment.CurrentDirectory;
        _testWorkspace = Path.Combine(Path.GetTempPath(), $"codepunk_glob_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testWorkspace);
        Environment.CurrentDirectory = _testWorkspace;
    }

    [Fact]
    public async Task Glob_SimpleWildcard_MatchesFiles()
    {
        var tool = new GlobTool();
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "test1.txt"), "1");
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "test2.txt"), "2");
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "other.log"), "3");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""*.txt"",
            ""path"": ""{_testWorkspace.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("Found 2 file(s)");
        result.Content.Should().Contain("test1.txt");
        result.Content.Should().Contain("test2.txt");
        result.Content.Should().NotContain("other.log");
    }

    [Fact]
    public async Task Glob_RecursivePattern_SearchesSubdirectories()
    {
        var tool = new GlobTool();
        var subdir = Path.Combine(_testWorkspace, "src");
        Directory.CreateDirectory(subdir);
        var nestedDir = Path.Combine(subdir, "nested");
        Directory.CreateDirectory(nestedDir);

        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "root.cs"), "root");
        await File.WriteAllTextAsync(Path.Combine(subdir, "file1.cs"), "file1");
        await File.WriteAllTextAsync(Path.Combine(nestedDir, "file2.cs"), "file2");
        await File.WriteAllTextAsync(Path.Combine(nestedDir, "other.txt"), "other");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""**/*.cs"",
            ""path"": ""{_testWorkspace.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("Found 3 file(s)");
        result.Content.Should().Contain("root.cs");
        result.Content.Should().Contain("file1.cs");
        result.Content.Should().Contain("file2.cs");
        result.Content.Should().NotContain("other.txt");
    }

    [Fact]
    public async Task Glob_NoMatches_ReturnsEmpty()
    {
        var tool = new GlobTool();
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "file.txt"), "content");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""*.doesnotexist"",
            ""path"": ""{_testWorkspace.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("Found 0 file(s)");
    }

    [Fact]
    public async Task Glob_QuestionMarkWildcard_MatchesSingleChar()
    {
        var tool = new GlobTool();
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "file1.txt"), "1");
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "file2.txt"), "2");
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "file10.txt"), "10");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""file?.txt"",
            ""path"": ""{_testWorkspace.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("Found 2 file(s)");
        result.Content.Should().Contain("file1.txt");
        result.Content.Should().Contain("file2.txt");
        result.Content.Should().NotContain("file10.txt");
    }

    [Fact]
    public async Task Glob_CaseSensitive_RespectsCasing()
    {
        var tool = new GlobTool();
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "file.txt"), "lower");
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "FILE.TXT"), "upper");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""*.txt"",
            ""path"": ""{_testWorkspace.Replace("\\", "\\\\")}"",
            ""case_sensitive"": true
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("file.txt");
    }

    [Fact]
    public async Task Glob_CaseInsensitive_MatchesBothCases()
    {
        var tool = new GlobTool();
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "lowercase.txt"), "lower");
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "UPPERCASE.TXT"), "upper");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""*.txt"",
            ""path"": ""{_testWorkspace.Replace("\\", "\\\\")}"",
            ""case_sensitive"": false
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("Found 2 file(s)");
        result.Content.Should().Contain("lowercase.txt");
        result.Content.Should().Contain("UPPERCASE.TXT");
    }

    [Fact]
    public async Task Glob_SortedByModificationTime_NewestFirst()
    {
        var tool = new GlobTool();
        var oldFile = Path.Combine(_testWorkspace, "old.txt");
        var newFile = Path.Combine(_testWorkspace, "new.txt");

        await File.WriteAllTextAsync(oldFile, "old");
        await Task.Delay(100);
        await File.WriteAllTextAsync(newFile, "new");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""*.txt"",
            ""path"": ""{_testWorkspace.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        var lines = result.Content.Split('\n').Where(l => l.EndsWith(".txt")).ToArray();
        lines[0].Should().Contain("new.txt");
        lines[1].Should().Contain("old.txt");
    }

    [Fact]
    public async Task Glob_NonExistentDirectory_ReturnsError()
    {
        var tool = new GlobTool();
        var nonExistent = Path.Combine(_testWorkspace, "doesnotexist");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""*.txt"",
            ""path"": ""{nonExistent.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeTrue();
        result.ErrorMessage.Should().Contain("does not exist");
    }

    [Fact]
    public async Task Glob_EmptyPattern_ReturnsError()
    {
        var tool = new GlobTool();

        var arguments = JsonDocument.Parse(@"{
            ""pattern"": """"
        }").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeTrue();
        result.ErrorMessage.Should().Contain("cannot be empty");
    }

    [Fact]
    public async Task Glob_DirectoryPattern_MatchesInSubdirectory()
    {
        var tool = new GlobTool();
        var srcDir = Path.Combine(_testWorkspace, "src");
        Directory.CreateDirectory(srcDir);
        await File.WriteAllTextAsync(Path.Combine(srcDir, "app.cs"), "code");
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "other.cs"), "other");

        var arguments = JsonDocument.Parse($@"{{
            ""pattern"": ""src/*.cs"",
            ""path"": ""{_testWorkspace.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("Found 1 file(s)");
        result.Content.Should().Contain("app.cs");
        result.Content.Should().NotContain("other.cs");
    }

    public void Dispose()
    {
        if (Directory.Exists(_originalDirectory))
        {
            Environment.CurrentDirectory = _originalDirectory;
        }
        if (Directory.Exists(_testWorkspace))
        {
            Directory.Delete(_testWorkspace, recursive: true);
        }
    }
}
