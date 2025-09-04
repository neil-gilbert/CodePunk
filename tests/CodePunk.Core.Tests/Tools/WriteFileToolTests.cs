using System.Text.Json;
using CodePunk.Core.Tools;
using FluentAssertions;
using Xunit;

namespace CodePunk.Core.Tests.Tools;

public class WriteFileToolTests
{
    [Fact]
    public async Task WriteFile_WritesToRelativePath_AndCreatesDirectories()
    {
        // arrange
        var tempDir = Directory.CreateTempSubdirectory();
        var prevCwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(tempDir.FullName);
            var tool = new WriteFileTool();
            var relPath = Path.Combine("subdir", "test.txt");
            var content = "hello world";

            var args = JsonSerializer.SerializeToElement(new { path = relPath, content });

            // act
            var result = await tool.ExecuteAsync(args);

            // assert
            result.IsError.Should().BeFalse(result.ErrorMessage);
            var fullPath = Path.GetFullPath(relPath);
            File.Exists(fullPath).Should().BeTrue();
            (await File.ReadAllTextAsync(fullPath)).Should().Be(content);
        }
        finally
        {
            Directory.SetCurrentDirectory(prevCwd);
            try { tempDir.Delete(true); } catch { }
        }
    }

    [Fact]
    public async Task WriteFile_AcceptsFilePathAlias()
    {
        // arrange
        var tempDir = Directory.CreateTempSubdirectory();
        var prevCwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(tempDir.FullName);
            var tool = new WriteFileTool();
            var relPath = "alias.txt";
            var content = "alias";

            var args = JsonSerializer.SerializeToElement(new { file_path = relPath, content });

            // act
            var result = await tool.ExecuteAsync(args);

            // assert
            result.IsError.Should().BeFalse(result.ErrorMessage);
            var fullPath = Path.GetFullPath(relPath);
            File.Exists(fullPath).Should().BeTrue();
            (await File.ReadAllTextAsync(fullPath)).Should().Be(content);
        }
        finally
        {
            Directory.SetCurrentDirectory(prevCwd);
            try { tempDir.Delete(true); } catch { }
        }
    }
}
