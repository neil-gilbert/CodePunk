using System.Text.Json;
using CodePunk.Core.Tools;
using FluentAssertions;
using Xunit;

namespace CodePunk.ComponentTests;

[Collection("Sequential")]
public class ReadFileToolTests : IDisposable
{
    private readonly string _testWorkspace;

    public ReadFileToolTests()
    {
        _testWorkspace = Path.Combine(Path.GetTempPath(), $"codepunk_readfile_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testWorkspace);
        Environment.CurrentDirectory = _testWorkspace;
    }

    [Fact]
    public async Task ReadFile_SmallFile_ReturnsCompleteContent()
    {
        var tool = new ReadFileTool();
        var fileName = Path.Combine(_testWorkspace, "small.txt");
        var expectedContent = "Line 1\nLine 2\nLine 3";
        await File.WriteAllTextAsync(fileName, expectedContent);

        var arguments = JsonDocument.Parse($@"{{
            ""path"": ""{fileName.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Be(expectedContent);
    }

    [Fact]
    public async Task ReadFile_WithOffset_ReturnsLinesFromOffset()
    {
        var tool = new ReadFileTool();
        var fileName = Path.Combine(_testWorkspace, "numbered.txt");
        var lines = Enumerable.Range(1, 100).Select(i => $"Line {i}").ToArray();
        await File.WriteAllLinesAsync(fileName, lines);

        var arguments = JsonDocument.Parse($@"{{
            ""path"": ""{fileName.Replace("\\", "\\\\")}"",
            ""offset"": 10,
            ""limit"": 5
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("lines 11-15 of 100");
        result.Content.Should().Contain("Line 11");
        result.Content.Should().Contain("Line 15");
        result.Content.Should().NotContain("Line 10");
        result.Content.Should().NotContain("Line 16");
    }

    [Fact]
    public async Task ReadFile_WithLimit_ShowsPaginationMessage()
    {
        var tool = new ReadFileTool();
        var fileName = Path.Combine(_testWorkspace, "large.txt");
        var lines = Enumerable.Range(1, 200).Select(i => $"Line {i}").ToArray();
        await File.WriteAllLinesAsync(fileName, lines);

        var arguments = JsonDocument.Parse($@"{{
            ""path"": ""{fileName.Replace("\\", "\\\\")}"",
            ""offset"": 0,
            ""limit"": 50
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("IMPORTANT: The file content has been paginated");
        result.Content.Should().Contain("Showing lines 1-50 of 200 total lines");
        result.Content.Should().Contain("use offset: 50");
    }

    [Fact]
    public async Task ReadFile_OffsetBeyondFileEnd_ReturnsError()
    {
        var tool = new ReadFileTool();
        var fileName = Path.Combine(_testWorkspace, "short.txt");
        await File.WriteAllLinesAsync(fileName, new[] { "Line 1", "Line 2" });

        var arguments = JsonDocument.Parse($@"{{
            ""path"": ""{fileName.Replace("\\", "\\\\")}"",
            ""offset"": 10
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeTrue();
        result.ErrorMessage.Should().Contain("Offset out of range");
    }

    [Fact]
    public async Task ReadFile_NegativeOffset_ReturnsError()
    {
        var tool = new ReadFileTool();
        var fileName = Path.Combine(_testWorkspace, "test.txt");
        await File.WriteAllTextAsync(fileName, "content");

        var arguments = JsonDocument.Parse($@"{{
            ""path"": ""{fileName.Replace("\\", "\\\\")}"",
            ""offset"": -1
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeTrue();
        result.ErrorMessage.Should().Contain("Invalid offset value");
    }

    [Fact]
    public async Task ReadFile_ZeroLimit_ReturnsError()
    {
        var tool = new ReadFileTool();
        var fileName = Path.Combine(_testWorkspace, "test.txt");
        await File.WriteAllTextAsync(fileName, "content");

        var arguments = JsonDocument.Parse($@"{{
            ""path"": ""{fileName.Replace("\\", "\\\\")}"",
            ""limit"": 0
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeTrue();
        result.ErrorMessage.Should().Contain("Invalid limit value");
    }

    [Fact]
    public async Task ReadFile_LastPage_NoNextOffsetSuggestion()
    {
        var tool = new ReadFileTool();
        var fileName = Path.Combine(_testWorkspace, "final.txt");
        var lines = Enumerable.Range(1, 10).Select(i => $"Line {i}").ToArray();
        await File.WriteAllLinesAsync(fileName, lines);

        var arguments = JsonDocument.Parse($@"{{
            ""path"": ""{fileName.Replace("\\", "\\\\")}"",
            ""offset"": 5,
            ""limit"": 10
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("lines 6-10 of 10");
        result.Content.Should().NotContain("use offset:");
    }

    [Fact]
    public async Task ReadFile_VeryLongLine_TruncatesLine()
    {
        var tool = new ReadFileTool();
        var fileName = Path.Combine(_testWorkspace, "longline.txt");
        var longLine = new string('A', 3000);
        await File.WriteAllTextAsync(fileName, longLine);

        var arguments = JsonDocument.Parse($@"{{
            ""path"": ""{fileName.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("[line truncated]");
        result.Content.Length.Should().BeLessThan(3000);
    }

    [Fact]
    public async Task ReadFile_NonExistentFile_ReturnsError()
    {
        var tool = new ReadFileTool();
        var fileName = Path.Combine(_testWorkspace, "doesnotexist.txt");

        var arguments = JsonDocument.Parse($@"{{
            ""path"": ""{fileName.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeTrue();
        result.ErrorMessage.Should().Contain("does not exist");
    }

    [Fact]
    public async Task ReadFile_EmptyPath_ReturnsError()
    {
        var tool = new ReadFileTool();

        var arguments = JsonDocument.Parse(@"{
            ""path"": """"
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
