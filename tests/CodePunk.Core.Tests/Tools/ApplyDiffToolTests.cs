/*
using System.Text.Json;
using CodePunk.Core.Tools;
using FluentAssertions;
using Xunit;

namespace CodePunk.Core.Tests.Tools;

[ignore]
public class ApplyDiffToolTests
{
    [Fact]
    public async Task ApplyDiffTool_AppliesUnifiedDiffToTextFile()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        var prevCwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(tempDir.FullName);
            var file = "test.txt";
            var original = "line1\nline2\nline3";
            await File.WriteAllTextAsync(file, original);
            var patch = "@@ -1,3 +1,3 @@\n line1\n-line2\n+LINE2\n line3";
            var tool = new ApplyDiffTool();
            var args = JsonSerializer.SerializeToElement(new {
                filePath = file,
                patch = patch,
                patchFormat = "unified",
                strategy = "strict"
            });
            var result = await tool.ExecuteAsync(args);
            result.IsError.Should().BeFalse(result.ErrorMessage);
            var newContent = await File.ReadAllTextAsync(file);
            newContent.Should().Be("line1\nLINE2\nline3");
        }
        finally
        {
            Directory.SetCurrentDirectory(prevCwd);
            try { tempDir.Delete(true); } catch { }
        }
    }

    [Fact]
    public async Task ApplyDiffTool_RejectsBinaryFile()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        var prevCwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(tempDir.FullName);
            var file = "bin.dat";
            await File.WriteAllBytesAsync(file, new byte[] { 0, 1, 2, 3 });
            var patch = "@@ -1,0 +1,1 @@\n+foo";
            var tool = new ApplyDiffTool();
            var args = JsonSerializer.SerializeToElement(new {
                filePath = file,
                patch = patch,
                patchFormat = "unified",
                strategy = "strict"
            });
            var result = await tool.ExecuteAsync(args);
            result.IsError.Should().BeTrue();
            result.ErrorMessage.Should().Contain("binary");
        }
        finally
        {
            Directory.SetCurrentDirectory(prevCwd);
            try { tempDir.Delete(true); } catch { }
        }
    }

    [Fact]
    public async Task ApplyDiffTool_RejectsContextMismatch()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        var prevCwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(tempDir.FullName);
            var file = "test2.txt";
            await File.WriteAllTextAsync(file, "a\nb\nc");
            var patch = "@@ -1,3 +1,3 @@\n x\n-y\n+Y\n z";
            var tool = new ApplyDiffTool();
            var args = JsonSerializer.SerializeToElement(new {
                filePath = file,
                patch = patch,
                patchFormat = "unified",
                strategy = "strict"
            });
            var result = await tool.ExecuteAsync(args);
            result.IsError.Should().BeTrue();
            result.ErrorMessage.Should().Contain("Context mismatch");
        }
        finally
        {
            Directory.SetCurrentDirectory(prevCwd);
            try { if (tempDir.Exists) tempDir.Delete(true); } catch { }
        }
    }
}
*/
