using System.Text.Json;
using CodePunk.ComponentTests.TestHelpers;
using CodePunk.Core.Tools;
using FluentAssertions;
using Xunit;

namespace CodePunk.ComponentTests;

[Collection("Sequential")]
public class ListDirectoryToolTests : WorkspaceTestBase
{
    public ListDirectoryToolTests() : base("listdir_test")
    {
    }

    [Fact]
    public async Task ListDirectory_EmptyDirectory_ReturnsEmptyList()
    {
        var tool = new ListDirectoryTool();
        var emptyDir = Path.Combine(TestWorkspace, "empty");
        Directory.CreateDirectory(emptyDir);

        var arguments = JsonDocument.Parse($@"{{
            ""path"": ""{emptyDir.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("Total entries: 0");
    }

    [Fact]
    public async Task ListDirectory_WithFiles_ReturnsFileList()
    {
        var tool = new ListDirectoryTool();
        var testDir = Path.Combine(TestWorkspace, "files");
        Directory.CreateDirectory(testDir);
        await File.WriteAllTextAsync(Path.Combine(testDir, "file1.txt"), "content1");
        await File.WriteAllTextAsync(Path.Combine(testDir, "file2.txt"), "content2");

        var arguments = JsonDocument.Parse($@"{{
            ""path"": ""{testDir.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("[FILE] file1.txt");
        result.Content.Should().Contain("[FILE] file2.txt");
        result.Content.Should().Contain("Total entries: 2");
    }

    [Fact]
    public async Task ListDirectory_WithDirectories_ListsDirectoriesFirst()
    {
        var tool = new ListDirectoryTool();
        var testDir = Path.Combine(TestWorkspace, "mixed");
        Directory.CreateDirectory(testDir);
        Directory.CreateDirectory(Path.Combine(testDir, "subdir1"));
        Directory.CreateDirectory(Path.Combine(testDir, "subdir2"));
        await File.WriteAllTextAsync(Path.Combine(testDir, "afile.txt"), "content");

        var arguments = JsonDocument.Parse($@"{{
            ""path"": ""{testDir.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("[DIR]  subdir1");
        result.Content.Should().Contain("[DIR]  subdir2");
        result.Content.Should().Contain("[FILE] afile.txt");

        var lines = result.Content.Split('\n');
        var subdirIndex = Array.FindIndex(lines, l => l.Contains("subdir1"));
        var fileIndex = Array.FindIndex(lines, l => l.Contains("afile.txt"));
        subdirIndex.Should().BeLessThan(fileIndex);
    }

    [Fact]
    public async Task ListDirectory_WithIgnorePattern_FiltersMatchingEntries()
    {
        var tool = new ListDirectoryTool();
        var testDir = Path.Combine(TestWorkspace, "ignore");
        Directory.CreateDirectory(testDir);
        await File.WriteAllTextAsync(Path.Combine(testDir, "keep.txt"), "keep");
        await File.WriteAllTextAsync(Path.Combine(testDir, "ignore.log"), "ignore");
        await File.WriteAllTextAsync(Path.Combine(testDir, "ignore2.log"), "ignore");

        var arguments = JsonDocument.Parse($@"{{
            ""path"": ""{testDir.Replace("\\", "\\\\")}"",
            ""ignore"": [""*.log""]
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("keep.txt");
        result.Content.Should().NotContain("ignore.log");
        result.Content.Should().NotContain("ignore2.log");
        result.Content.Should().Contain("Total entries: 1");
    }

    [Fact]
    public async Task ListDirectory_WithMultipleIgnorePatterns_FiltersAll()
    {
        var tool = new ListDirectoryTool();
        var testDir = Path.Combine(TestWorkspace, "multiignore");
        Directory.CreateDirectory(testDir);
        await File.WriteAllTextAsync(Path.Combine(testDir, "keep.txt"), "keep");
        await File.WriteAllTextAsync(Path.Combine(testDir, "temp.log"), "temp");
        Directory.CreateDirectory(Path.Combine(testDir, ".git"));

        var arguments = JsonDocument.Parse($@"{{
            ""path"": ""{testDir.Replace("\\", "\\\\")}"",
            ""ignore"": [""*.log"", "".git""]
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("keep.txt");
        result.Content.Should().NotContain("temp.log");
        result.Content.Should().NotContain(".git");
        result.Content.Should().Contain("Total entries: 1");
    }

    [Fact]
    public async Task ListDirectory_ShowsFileSizes()
    {
        var tool = new ListDirectoryTool();
        var testDir = Path.Combine(TestWorkspace, "sizes");
        Directory.CreateDirectory(testDir);
        var content = new string('A', 1024);
        await File.WriteAllTextAsync(Path.Combine(testDir, "1kb.txt"), content);

        var arguments = JsonDocument.Parse($@"{{
            ""path"": ""{testDir.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("1kb.txt");
        result.Content.Should().MatchRegex(@"\d+(\.\d+)?\s+(B|KB|MB)");
    }

    [Fact]
    public async Task ListDirectory_ShowsModifiedTime()
    {
        var tool = new ListDirectoryTool();
        var testDir = Path.Combine(TestWorkspace, "times");
        Directory.CreateDirectory(testDir);
        await File.WriteAllTextAsync(Path.Combine(testDir, "dated.txt"), "content");

        var arguments = JsonDocument.Parse($@"{{
            ""path"": ""{testDir.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("modified:");
        result.Content.Should().MatchRegex(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}");
    }

    [Fact]
    public async Task ListDirectory_NonExistentDirectory_ReturnsError()
    {
        var tool = new ListDirectoryTool();
        var nonExistent = Path.Combine(TestWorkspace, "doesnotexist");

        var arguments = JsonDocument.Parse($@"{{
            ""path"": ""{nonExistent.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeTrue();
        result.ErrorMessage.Should().Contain("does not exist");
    }

    [Fact]
    public async Task ListDirectory_EmptyPath_ReturnsError()
    {
        var tool = new ListDirectoryTool();

        var arguments = JsonDocument.Parse(@"{
            ""path"": """"
        }").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeTrue();
        result.ErrorMessage.Should().Contain("cannot be empty");
    }

    [Fact]
    public async Task ListDirectory_SortedAlphabetically_WithinTypes()
    {
        var tool = new ListDirectoryTool();
        var testDir = Path.Combine(TestWorkspace, "sorted");
        Directory.CreateDirectory(testDir);
        await File.WriteAllTextAsync(Path.Combine(testDir, "zebra.txt"), "z");
        await File.WriteAllTextAsync(Path.Combine(testDir, "alpha.txt"), "a");
        await File.WriteAllTextAsync(Path.Combine(testDir, "beta.txt"), "b");

        var arguments = JsonDocument.Parse($@"{{
            ""path"": ""{testDir.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        var lines = result.Content.Split('\n').Where(l => l.Contains("[FILE]")).ToArray();
        lines[0].Should().Contain("alpha.txt");
        lines[1].Should().Contain("beta.txt");
        lines[2].Should().Contain("zebra.txt");
    }

}
