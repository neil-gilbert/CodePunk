using CodePunk.Core.Git;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodePunk.ComponentTests;

public class GitCommandExecutorTests : IDisposable
{
    private readonly string _testWorkspace;
    private readonly GitCommandExecutor _executor;

    public GitCommandExecutorTests()
    {
        _testWorkspace = Path.Combine(Path.GetTempPath(), $"codepunk_git_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testWorkspace);

        var workingDirProvider = new FixedWorkingDirectoryProvider(_testWorkspace);
        _executor = new GitCommandExecutor(NullLogger<GitCommandExecutor>.Instance, workingDirProvider);

        InitializeGitRepo();
    }

    [Fact]
    public async Task ExecuteAsync_ValidCommand_ReturnsSuccess()
    {
        var result = await _executor.ExecuteAsync("status");

        result.Success.Should().BeTrue();
        result.Output.Should().NotBeEmpty();
        result.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidCommand_ReturnsFailure()
    {
        var result = await _executor.ExecuteAsync("invalid-git-command-xyz");

        result.Success.Should().BeFalse();
        result.ExitCode.Should().NotBe(0);
    }

    [Fact]
    public async Task GetCurrentBranchAsync_OnMainBranch_ReturnsMain()
    {
        var result = await _executor.GetCurrentBranchAsync();

        result.Success.Should().BeTrue();
        result.Value.Should().Be("main");
    }

    [Fact]
    public async Task IsGitRepositoryAsync_InGitRepo_ReturnsTrue()
    {
        var result = await _executor.IsGitRepositoryAsync();

        result.Success.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task HasUncommittedChangesAsync_NoChanges_ReturnsFalse()
    {
        var result = await _executor.HasUncommittedChangesAsync();

        result.Success.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task HasUncommittedChangesAsync_WithChanges_ReturnsTrue()
    {
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "test.txt"), "content");

        var result = await _executor.HasUncommittedChangesAsync();

        result.Success.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task GetModifiedFilesAsync_WithModifications_ReturnsFiles()
    {
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "file1.txt"), "content1");
        await _executor.ExecuteAsync("add file1.txt");
        await _executor.ExecuteAsync("commit -m \"Add file1\"");

        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "file1.txt"), "modified");
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "file2.txt"), "content2");

        var result = await _executor.GetModifiedFilesAsync();

        result.Success.Should().BeTrue();
        result.Value.Should().Contain("file1.txt");
    }

    private void InitializeGitRepo()
    {
        _executor.ExecuteAsync("init -b main").Wait();
        _executor.ExecuteAsync("config user.email \"test@codepunk.ai\"").Wait();
        _executor.ExecuteAsync("config user.name \"Test User\"").Wait();

        File.WriteAllText(Path.Combine(_testWorkspace, "README.md"), "# Test Repo");
        _executor.ExecuteAsync("add README.md").Wait();
        _executor.ExecuteAsync("commit -m \"Initial commit\"").Wait();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testWorkspace))
        {
            try
            {
                Directory.Delete(_testWorkspace, recursive: true);
            }
            catch { }
        }
    }
}

internal class FixedWorkingDirectoryProvider : IWorkingDirectoryProvider
{
    private readonly string _directory;

    public FixedWorkingDirectoryProvider(string directory)
    {
        _directory = directory;
    }

    public string GetWorkingDirectory() => _directory;
}
