using CodePunk.Core.Checkpointing;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CodePunk.ComponentTests;

public class CheckpointServiceTests : IAsyncLifetime
{
    private string _testWorkspace = null!;
    private CheckpointService _service = null!;

    public async Task InitializeAsync()
    {
        _testWorkspace = Path.Combine(Path.GetTempPath(), $"codepunk_checkpoint_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testWorkspace);

        var options = new CheckpointOptions
        {
            Enabled = true,
            CheckpointDirectory = Path.Combine(_testWorkspace, ".checkpoints")
        };

        _service = new CheckpointService(
            Options.Create(options),
            new GitCommandExecutor());

        await _service.InitializeAsync(_testWorkspace);
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
    public async Task InitializeAsync_CreatesGitRepository()
    {
        var checkpointBase = Path.Combine(_testWorkspace, ".checkpoints");
        var subdirs = Directory.GetDirectories(checkpointBase);

        subdirs.Should().HaveCount(1);

        var shadowRepoPath = subdirs[0];
        var gitDir = Path.Combine(shadowRepoPath, ".git");

        Directory.Exists(gitDir).Should().BeTrue();
    }

    [Fact]
    public async Task CreateCheckpointAsync_WithNoChanges_CreatesInitialCheckpoint()
    {
        var result = await _service.CreateCheckpointAsync(
            "tool_123",
            "write_file",
            "Initial checkpoint");

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateCheckpointAsync_WithFileChanges_CapturesChanges()
    {
        var testFile = Path.Combine(_testWorkspace, "test.txt");
        await File.WriteAllTextAsync(testFile, "Original content");

        var result = await _service.CreateCheckpointAsync(
            "tool_456",
            "write_file",
            "Before modifying test.txt");

        result.Success.Should().BeTrue();

        var checkpoint = await _service.GetCheckpointAsync(result.Data!);
        checkpoint.Data!.ModifiedFiles.Should().Contain("test.txt");
    }

    [Fact]
    public async Task RestoreCheckpointAsync_RestoresFileState()
    {
        var testFile = Path.Combine(_testWorkspace, "test.txt");
        await File.WriteAllTextAsync(testFile, "Original content");

        var checkpoint = await _service.CreateCheckpointAsync(
            "tool_789",
            "write_file",
            "Before change");

        await File.WriteAllTextAsync(testFile, "Modified content");

        var restoreResult = await _service.RestoreCheckpointAsync(checkpoint.Data!);

        restoreResult.Success.Should().BeTrue();
        var restoredContent = await File.ReadAllTextAsync(testFile);
        restoredContent.Should().Be("Original content");
    }

    [Fact]
    public async Task ListCheckpointsAsync_ReturnsCheckpointsInReverseChronologicalOrder()
    {
        await _service.CreateCheckpointAsync("tool_1", "test", "First");
        await Task.Delay(100);
        await _service.CreateCheckpointAsync("tool_2", "test", "Second");
        await Task.Delay(100);
        await _service.CreateCheckpointAsync("tool_3", "test", "Third");

        var result = await _service.ListCheckpointsAsync(limit: 10);

        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(3);
        result.Data![0].Description.Should().Be("Third");
        result.Data[1].Description.Should().Be("Second");
        result.Data[2].Description.Should().Be("First");
    }

    [Fact]
    public async Task PruneCheckpointsAsync_DeletesOldestCheckpoints()
    {
        for (int i = 0; i < 10; i++)
        {
            await _service.CreateCheckpointAsync($"tool_{i}", "test", $"Checkpoint {i}");
        }

        var pruneResult = await _service.PruneCheckpointsAsync(keepCount: 5);

        pruneResult.Success.Should().BeTrue();

        var remaining = await _service.ListCheckpointsAsync();
        remaining.Data.Should().HaveCount(5);
    }

    [Fact]
    public async Task RestoreCheckpointAsync_WithInvalidId_ReturnsError()
    {
        var result = await _service.RestoreCheckpointAsync("nonexistent_id");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }
}
