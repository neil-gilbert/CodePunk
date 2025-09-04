using System.Text.Json;
using CodePunk.Core.Tools;
using Xunit;

namespace CodePunk.Core.Tests.Tools;

/// <summary>
/// Direct test of WriteFileTool to verify it works
/// </summary>
public class WriteFileToolDirectTest
{
    [Fact]
    public async Task WriteFileTool_ShouldCreateFile()
    {
        // Arrange
        var tool = new WriteFileTool();
        var testPath = Path.Combine(Path.GetTempPath(), "test_write_tool.py");
        var testContent = "print('Hello from direct tool test!')";
        
        var arguments = JsonSerializer.SerializeToElement(new
        {
            path = testPath,
            content = testContent
        });
        
        // Clean up any existing file
        if (File.Exists(testPath))
            File.Delete(testPath);
        
        // Act
        var result = await tool.ExecuteAsync(arguments);
        
        // Assert
        Assert.False(result.IsError, $"Tool execution failed: {result.ErrorMessage}");
        Assert.Contains("Successfully wrote to", result.Content);
        
        // Verify file was actually created
        Assert.True(File.Exists(testPath), "File was not created on disk");
        
        var actualContent = await File.ReadAllTextAsync(testPath);
        Assert.Equal(testContent, actualContent);
        
        // Cleanup
        File.Delete(testPath);
    }
    
    [Fact]
    public async Task WriteFileTool_ShouldCreateDirectoryIfNotExists()
    {
        // Arrange
        var tool = new WriteFileTool();
        var testDir = Path.Combine(Path.GetTempPath(), "test_dir_" + Guid.NewGuid().ToString("N")[..8]);
        var testPath = Path.Combine(testDir, "subdirectory", "test.py");
        var testContent = "print('Hello from nested directory!')";
        
        var arguments = JsonSerializer.SerializeToElement(new
        {
            path = testPath,
            content = testContent
        });
        
        // Ensure directory doesn't exist
        if (Directory.Exists(testDir))
            Directory.Delete(testDir, true);
        
        // Act
        var result = await tool.ExecuteAsync(arguments);
        
        // Assert
        Assert.False(result.IsError, $"Tool execution failed: {result.ErrorMessage}");
        Assert.True(File.Exists(testPath), "File was not created in nested directory");
        
        var actualContent = await File.ReadAllTextAsync(testPath);
        Assert.Equal(testContent, actualContent);
        
        // Cleanup
        Directory.Delete(testDir, true);
    }
}
