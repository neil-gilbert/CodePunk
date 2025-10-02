using System.Text.Json;
using CodePunk.Core.Tools;
using FluentAssertions;
using Xunit;

namespace CodePunk.ComponentTests;

[Collection("Sequential")]
public class ReadManyFilesToolTests : IDisposable
{
    private readonly string _testWorkspace;
    private readonly string _originalDirectory;

    public ReadManyFilesToolTests()
    {
        _originalDirectory = Environment.CurrentDirectory;
        _testWorkspace = Path.Combine(Path.GetTempPath(), $"codepunk_readmany_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testWorkspace);
        Environment.CurrentDirectory = _testWorkspace;
    }

    [Fact]
    public async Task ReadManyFiles_ExplicitPaths_ReadsAllFiles()
    {
        var tool = new ReadManyFilesTool();
        var file1 = Path.Combine(_testWorkspace, "file1.txt");
        var file2 = Path.Combine(_testWorkspace, "file2.txt");
        await File.WriteAllTextAsync(file1, "Content 1");
        await File.WriteAllTextAsync(file2, "Content 2");

        var arguments = JsonDocument.Parse($@"{{
            ""paths"": [""{file1.Replace("\\", "\\\\")}"", ""{file2.Replace("\\", "\\\\")}""]
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("Read 2 file(s) successfully");
        result.Content.Should().Contain("file1.txt");
        result.Content.Should().Contain("Content 1");
        result.Content.Should().Contain("file2.txt");
        result.Content.Should().Contain("Content 2");
    }

    [Fact]
    public async Task ReadManyFiles_GlobPattern_ReadsMatchingFiles()
    {
        var tool = new ReadManyFilesTool();
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "test1.txt"), "Test 1");
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "test2.txt"), "Test 2");
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "other.log"), "Other");

        var pattern = Path.Combine(_testWorkspace, "*.txt");
        var arguments = JsonDocument.Parse($@"{{
            ""paths"": [""{pattern.Replace("\\", "\\\\")}""]
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("Read 2 file(s) successfully");
        result.Content.Should().Contain("test1.txt");
        result.Content.Should().Contain("test2.txt");
        result.Content.Should().NotContain("other.log");
    }

    [Fact]
    public async Task ReadManyFiles_RecursivePattern_ReadsNestedFiles()
    {
        var tool = new ReadManyFilesTool();
        var subdir = Path.Combine(_testWorkspace, "src");
        Directory.CreateDirectory(subdir);
        var nested = Path.Combine(subdir, "nested");
        Directory.CreateDirectory(nested);

        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "root.cs"), "Root");
        await File.WriteAllTextAsync(Path.Combine(subdir, "file1.cs"), "File1");
        await File.WriteAllTextAsync(Path.Combine(nested, "file2.cs"), "File2");

        var pattern = Path.Combine(_testWorkspace, "**/*.cs");
        var arguments = JsonDocument.Parse($@"{{
            ""paths"": [""{pattern.Replace("\\", "\\\\")}""]
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("Read 3 file(s) successfully");
        result.Content.Should().Contain("root.cs");
        result.Content.Should().Contain("file1.cs");
        result.Content.Should().Contain("file2.cs");
    }

    [Fact]
    public async Task ReadManyFiles_WithExcludePattern_FiltersFiles()
    {
        var tool = new ReadManyFilesTool();
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "keep.txt"), "Keep");
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "exclude.txt"), "Exclude");
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "exclude2.txt"), "Exclude2");

        var arguments = JsonDocument.Parse(@"{
            ""paths"": [""*.txt""],
            ""exclude"": [""exclude*""]
        }").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("Read 1 file(s) successfully");
        result.Content.Should().Contain("keep.txt");
        result.Content.Should().NotContain("exclude.txt");
    }

    [Fact]
    public async Task ReadManyFiles_MixedPathsAndPatterns_ReadsAll()
    {
        var tool = new ReadManyFilesTool();
        var explicitFile = Path.Combine(_testWorkspace, "explicit.txt");
        await File.WriteAllTextAsync(explicitFile, "Explicit");
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "pattern1.log"), "Pattern1");
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "pattern2.log"), "Pattern2");

        var pattern = Path.Combine(_testWorkspace, "*.log");
        var arguments = JsonDocument.Parse($@"{{
            ""paths"": [""{explicitFile.Replace("\\", "\\\\")}"", ""{pattern.Replace("\\", "\\\\")}""]
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("Read 3 file(s) successfully");
        result.Content.Should().Contain("explicit.txt");
        result.Content.Should().Contain("pattern1.log");
        result.Content.Should().Contain("pattern2.log");
    }

    [Fact]
    public async Task ReadManyFiles_NonExistentFile_ReportsFailure()
    {
        var tool = new ReadManyFilesTool();
        var goodFile = Path.Combine(_testWorkspace, "good.txt");
        var badFile = Path.Combine(_testWorkspace, "bad.txt");
        await File.WriteAllTextAsync(goodFile, "Good");

        var arguments = JsonDocument.Parse($@"{{
            ""paths"": [""{goodFile.Replace("\\", "\\\\")}"", ""{badFile.Replace("\\", "\\\\")}""]
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("Read 1 file(s) successfully");
        result.Content.Should().Contain("Failed to read 1 file(s)");
        result.Content.Should().Contain("good.txt");
        result.Content.Should().Contain("bad.txt");
        result.Content.Should().Contain("[Error reading file:");
    }

    [Fact]
    public async Task ReadManyFiles_NoMatchingFiles_ReturnsMessage()
    {
        var tool = new ReadManyFilesTool();

        var arguments = JsonDocument.Parse(@"{
            ""paths"": [""*.nonexistent""]
        }").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("No files matched");
    }

    [Fact]
    public async Task ReadManyFiles_EmptyPathsArray_ReturnsError()
    {
        var tool = new ReadManyFilesTool();

        var arguments = JsonDocument.Parse(@"{
            ""paths"": []
        }").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeTrue();
        result.ErrorMessage.Should().Contain("At least one path is required");
    }

    [Fact]
    public async Task ReadManyFiles_ShowsEndOfContentSeparator()
    {
        var tool = new ReadManyFilesTool();
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "file1.txt"), "Content 1");
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "file2.txt"), "Content 2");

        var pattern = Path.Combine(_testWorkspace, "*.txt");
        var arguments = JsonDocument.Parse($@"{{
            ""paths"": [""{pattern.Replace("\\", "\\\\")}""]
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("--- End of content ---");
        var separatorCount = result.Content.Split("--- End of content ---").Length - 1;
        separatorCount.Should().Be(2);
    }

    [Fact]
    public async Task ReadManyFiles_DirectoryPath_ReadsAllFilesInDirectory()
    {
        var tool = new ReadManyFilesTool();
        var subdir = Path.Combine(_testWorkspace, "mydir");
        Directory.CreateDirectory(subdir);
        await File.WriteAllTextAsync(Path.Combine(subdir, "file1.txt"), "File1");
        await File.WriteAllTextAsync(Path.Combine(subdir, "file2.txt"), "File2");

        var arguments = JsonDocument.Parse($@"{{
            ""paths"": [""{subdir.Replace("\\", "\\\\")}""]
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("Read 2 file(s) successfully");
        result.Content.Should().Contain("file1.txt");
        result.Content.Should().Contain("file2.txt");
    }

    [Fact]
    public async Task ReadManyFiles_ShowsRelativePaths()
    {
        var tool = new ReadManyFilesTool();
        var subdir = Path.Combine(_testWorkspace, "sub");
        Directory.CreateDirectory(subdir);
        await File.WriteAllTextAsync(Path.Combine(subdir, "nested.txt"), "Nested");

        var pattern = Path.Combine(_testWorkspace, "**/*.txt");
        var arguments = JsonDocument.Parse($@"{{
            ""paths"": [""{pattern.Replace("\\", "\\\\")}""]
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("sub");
        result.Content.Should().Contain("nested.txt");
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
