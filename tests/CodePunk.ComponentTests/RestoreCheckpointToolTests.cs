using CodePunk.Core.Checkpointing;
using CodePunk.Core.Tools;
using FluentAssertions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace CodePunk.ComponentTests;

public class RestoreCheckpointToolTests : IAsyncLifetime
{
    private string _testWorkspace = null!;
    private CheckpointService _checkpointService = null!;
    private RestoreCheckpointTool _tool = null!;

    public async Task InitializeAsync()
    {
        _testWorkspace = Path.Combine(Path.GetTempPath(), $"codepunk_tool_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testWorkspace);

        var options = new CheckpointOptions
        {
            Enabled = true,
            CheckpointDirectory = Path.Combine(_testWorkspace, ".checkpoints")
        };

        _checkpointService = new CheckpointService(
            Options.Create(options),
            new GitCommandExecutor());

        await _checkpointService.InitializeAsync(_testWorkspace);

        _tool = new RestoreCheckpointTool(_checkpointService);
    }

    public async Task DisposeAsync()
    {
        if (Directory.Exists(_testWorkspace))
        {
            Directory.Delete(_testWorkspace, recursive: true);
        }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ExecuteAsync_WithValidCheckpoint_RestoresFiles()
    {
        var testFile = Path.Combine(_testWorkspace, "test.txt");
        await File.WriteAllTextAsync(testFile, "Original content");

        var checkpoint = await _checkpointService.CreateCheckpointAsync(
            "tool_123",
            "write_file",
            "Before modification");

        await File.WriteAllTextAsync(testFile, "Modified content");

        var parameters = JsonSerializer.SerializeToElement(new { checkpoint_id = checkpoint.Data });
        var result = await _tool.ExecuteAsync(parameters);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("Successfully restored");
        result.Content.Should().Contain(checkpoint.Data!);

        var restoredContent = await File.ReadAllTextAsync(testFile);
        restoredContent.Should().Be("Original content");
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidCheckpoint_ReturnsError()
    {
        var parameters = JsonSerializer.SerializeToElement(new { checkpoint_id = "nonexistent" });
        var result = await _tool.ExecuteAsync(parameters);

        result.IsError.Should().BeTrue();
        result.Content.Should().Contain("not found");
    }

    [Fact]
    public async Task ExecuteAsync_IncludesCheckpointMetadata_InResponse()
    {
        var testFile = Path.Combine(_testWorkspace, "test.txt");
        await File.WriteAllTextAsync(testFile, "Content");

        var checkpoint = await _checkpointService.CreateCheckpointAsync(
            "tool_456",
            "smart_edit",
            "Test description");

        var parameters = JsonSerializer.SerializeToElement(new { checkpoint_id = checkpoint.Data });
        var result = await _tool.ExecuteAsync(parameters);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("smart_edit");
        result.Content.Should().Contain("Test description");
    }
}
