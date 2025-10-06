using CodePunk.Core.Git;
using CodePunk.Core.GitSession;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CodePunk.ComponentTests;

public class GitSessionBehaviorTests : IDisposable
{
    private readonly string _testWorkspace;
    private readonly GitCommandExecutor _gitExecutor;
    private readonly GitSessionStateStore _stateStore;
    private readonly GitSessionService _sessionService;
    private readonly GitSessionOptions _options;

    public GitSessionBehaviorTests()
    {
        _testWorkspace = Path.Combine(Path.GetTempPath(), $"codepunk_session_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testWorkspace);

        var sessionStateDir = Path.Combine(Path.GetTempPath(), $"codepunk_sessions_{Guid.NewGuid():N}");

        _options = new GitSessionOptions
        {
            Enabled = true,
            StateStorePath = sessionStateDir
        };

        var workingDirProvider = new FixedWorkingDirectoryProvider(_testWorkspace);
        _gitExecutor = new GitCommandExecutor(NullLogger<GitCommandExecutor>.Instance, workingDirProvider);
        _stateStore = new GitSessionStateStore(
            Options.Create(_options),
            NullLogger<GitSessionStateStore>.Instance);
        _sessionService = new GitSessionService(
            _gitExecutor,
            _stateStore,
            workingDirProvider,
            Options.Create(_options),
            NullLogger<GitSessionService>.Instance);

        InitializeGitRepo();
    }

    [Fact]
    public async Task SessionCreationAndAccept_LeavesFilesAsUnstagedModifications()
    {
        var session = await _sessionService.BeginSessionAsync();
        session.Should().NotBeNull();
        var worktreePath = session!.WorktreePath;

        // Write files to worktree (where session operates)
        await File.WriteAllTextAsync(Path.Combine(worktreePath, "file1.txt"), "content1");
        var commit1 = await _sessionService.CommitToolCallAsync("write_file", "Create file1.txt");
        commit1.Should().BeTrue("first commit should succeed");

        await File.WriteAllTextAsync(Path.Combine(worktreePath, "file2.txt"), "content2");
        var commit2 = await _sessionService.CommitToolCallAsync("write_file", "Create file2.txt");
        commit2.Should().BeTrue("second commit should succeed");

        await File.WriteAllTextAsync(Path.Combine(worktreePath, "file3.txt"), "content3");
        var commit3 = await _sessionService.CommitToolCallAsync("write_file", "Create file3.txt");
        commit3.Should().BeTrue("third commit should succeed");

        var accepted = await _sessionService.AcceptSessionAsync();

        accepted.Should().BeTrue("session accept should succeed");

        var currentBranch = await _gitExecutor.GetCurrentBranchAsync();
        currentBranch.Value.Should().Be("main");

        // Files should exist in user workspace (applied from worktree)
        File.Exists(Path.Combine(_testWorkspace, "file1.txt")).Should().BeTrue();
        File.Exists(Path.Combine(_testWorkspace, "file2.txt")).Should().BeTrue();
        File.Exists(Path.Combine(_testWorkspace, "file3.txt")).Should().BeTrue();

        // Files should be unstaged modifications (not committed)
        var statusResult = await _gitExecutor.ExecuteAsync("status --porcelain");
        statusResult.Output.Should().Contain("file1.txt");
        statusResult.Output.Should().Contain("file2.txt");
        statusResult.Output.Should().Contain("file3.txt");

        // Shadow branch should be deleted
        var branchesResult = await _gitExecutor.ExecuteAsync("branch");
        branchesResult.Output.Should().NotContain("ai/session");
    }

    [Fact]
    public async Task SessionRejection_DiscardsAllChanges()
    {
        // Create file in user workspace
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "original.txt"), "original");
        await _gitExecutor.ExecuteAsync("add original.txt");
        await _gitExecutor.ExecuteAsync("commit -m \"Add original\"");

        var session = await _sessionService.BeginSessionAsync();
        var worktreePath = session!.WorktreePath;

        // Write files to worktree
        await File.WriteAllTextAsync(Path.Combine(worktreePath, "file1.txt"), "content1");
        await _sessionService.CommitToolCallAsync("write_file", "Create file1");

        await File.WriteAllTextAsync(Path.Combine(worktreePath, "file2.txt"), "content2");
        await _sessionService.CommitToolCallAsync("write_file", "Create file2");

        var rejected = await _sessionService.RejectSessionAsync();

        rejected.Should().BeTrue();

        var currentBranch = await _gitExecutor.GetCurrentBranchAsync();
        currentBranch.Value.Should().Be("main");

        // User workspace should be untouched (files not applied)
        File.Exists(Path.Combine(_testWorkspace, "file1.txt")).Should().BeFalse();
        File.Exists(Path.Combine(_testWorkspace, "file2.txt")).Should().BeFalse();
        File.Exists(Path.Combine(_testWorkspace, "original.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task WorkspaceIsolation_UncommittedChangesUntouched()
    {
        // Create uncommitted file in user workspace
        await File.WriteAllTextAsync(Path.Combine(_testWorkspace, "uncommitted.txt"), "uncommitted work");

        var session = await _sessionService.BeginSessionAsync();
        session.Should().NotBeNull();
        session!.WorktreePath.Should().NotBeNull();

        // Create file in worktree (session workspace)
        await File.WriteAllTextAsync(Path.Combine(session.WorktreePath, "session-file.txt"), "session content");
        await _sessionService.CommitToolCallAsync("write_file", "Create session file");

        await _sessionService.AcceptSessionAsync();

        // Uncommitted file should still exist untouched in user workspace
        File.Exists(Path.Combine(_testWorkspace, "uncommitted.txt")).Should().BeTrue();
        File.ReadAllText(Path.Combine(_testWorkspace, "uncommitted.txt")).Should().Be("uncommitted work");

        // Session file should be applied to user workspace
        File.Exists(Path.Combine(_testWorkspace, "session-file.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task MultipleToolCalls_CreatesMultipleCommits()
    {
        var session = await _sessionService.BeginSessionAsync();
        var worktreePath = session!.WorktreePath;

        await File.WriteAllTextAsync(Path.Combine(worktreePath, "file1.txt"), "content1");
        await _sessionService.CommitToolCallAsync("write_file", "Create file1");

        await File.WriteAllTextAsync(Path.Combine(worktreePath, "file2.txt"), "content2");
        await _sessionService.CommitToolCallAsync("replace_in_file", "Modify file2");

        // Check commits in the worktree (which is on the shadow branch)
        var logResult = await _gitExecutor.ExecuteAsync("log --oneline", workingDirectory: worktreePath);
        var commitCount = logResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        // Should have: initial commit + 2 tool commits
        commitCount.Should().BeGreaterThan(2);
    }

    [Fact]
    public async Task SessionWithoutGitRepository_ReturnsNull()
    {
        var nonGitWorkspace = Path.Combine(Path.GetTempPath(), $"codepunk_nogit_{Guid.NewGuid():N}");
        Directory.CreateDirectory(nonGitWorkspace);

        try
        {
            var workingDirProvider = new FixedWorkingDirectoryProvider(nonGitWorkspace);
            var executor = new GitCommandExecutor(NullLogger<GitCommandExecutor>.Instance, workingDirProvider);
            var stateStore = new GitSessionStateStore(
                Options.Create(_options),
                NullLogger<GitSessionStateStore>.Instance);
            var service = new GitSessionService(
                executor,
                stateStore,
                workingDirProvider,
                Options.Create(_options),
                NullLogger<GitSessionService>.Instance);

            var session = await service.BeginSessionAsync();

            session.Should().BeNull();
        }
        finally
        {
            Directory.Delete(nonGitWorkspace, recursive: true);
        }
    }

    [Fact]
    public async Task NewSessionWithActiveSession_AutoRevertsOldSession()
    {
        var session1 = await _sessionService.BeginSessionAsync();
        session1.Should().NotBeNull();
        var worktree1Path = session1!.WorktreePath;

        await File.WriteAllTextAsync(Path.Combine(worktree1Path, "session1.txt"), "content1");
        await _sessionService.CommitToolCallAsync("write_file", "Create session1 file");

        // Starting new session should auto-revert the first session
        var session2 = await _sessionService.BeginSessionAsync();
        session2.Should().NotBeNull();
        session2!.SessionId.Should().NotBe(session1.SessionId);
        var worktree2Path = session2.WorktreePath;

        // User workspace should not have session1 file (session1 was reverted)
        File.Exists(Path.Combine(_testWorkspace, "session1.txt")).Should().BeFalse();

        await File.WriteAllTextAsync(Path.Combine(worktree2Path, "session2.txt"), "content2");
        await _sessionService.CommitToolCallAsync("write_file", "Create session2 file");

        await _sessionService.AcceptSessionAsync();

        // Only session2 file should be in user workspace
        File.Exists(Path.Combine(_testWorkspace, "session2.txt")).Should().BeTrue();
        File.Exists(Path.Combine(_testWorkspace, "session1.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task ToolCallWithNoChanges_DoesNotCreateCommit()
    {
        var session = await _sessionService.BeginSessionAsync();

        var committed = await _sessionService.CommitToolCallAsync("read_file", "Read some file");

        committed.Should().BeTrue();

        session!.ToolCallCommits.Should().BeEmpty();
    }

    [Fact]
    public async Task SessionState_PersistsAndLoads()
    {
        var session = await _sessionService.BeginSessionAsync();
        var sessionId = session!.SessionId;
        var worktreePath = session.WorktreePath;

        await File.WriteAllTextAsync(Path.Combine(worktreePath, "file1.txt"), "content");
        await _sessionService.CommitToolCallAsync("write_file", "Create file");

        var loadedSession = await _stateStore.LoadAsync(sessionId);

        loadedSession.Should().NotBeNull();
        loadedSession!.SessionId.Should().Be(sessionId);
        loadedSession.ToolCallCommits.Should().HaveCount(1);
        loadedSession.ToolCallCommits[0].ToolName.Should().Be("write_file");
    }

    private void InitializeGitRepo()
    {
        _gitExecutor.ExecuteAsync("init -b main").Wait();
        _gitExecutor.ExecuteAsync("config user.email \"test@codepunk.ai\"").Wait();
        _gitExecutor.ExecuteAsync("config user.name \"Test User\"").Wait();

        File.WriteAllText(Path.Combine(_testWorkspace, "README.md"), "# Test Repo");
        _gitExecutor.ExecuteAsync("add README.md").Wait();
        _gitExecutor.ExecuteAsync("commit -m \"Initial commit\"").Wait();
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
