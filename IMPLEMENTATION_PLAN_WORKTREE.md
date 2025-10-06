# Implementation Plan: Git Worktree-Based Sessions

## Overview
Migrate from shadow branch checkouts to isolated git worktrees for complete workspace isolation and crash safety.

## Current vs New Architecture

### Current (Shadow Branch)
```
User workspace: /Users/neil/Repo/CodePunk
  ├── On branch: ai/session-abc123 (switched!)
  ├── User files: potentially clobbered
  └── Risk: Orphaned on crash
```

### New (Worktree)
```
User workspace: /Users/neil/Repo/CodePunk
  ├── On branch: main (never changes!)
  └── User files: never touched

AI workspace: /tmp/codepunk-sessions/abc123
  ├── On branch: ai/session-abc123
  ├── Linked to main repo
  └── Cleaned up after session
```

## Benefits
- ✅ User workspace never changes unexpectedly
- ✅ No orphaned branch state on crash
- ✅ No stashing needed (user uncommitted changes safe)
- ✅ Editor-safe (no file conflicts)
- ✅ Could support parallel sessions in future
- ✅ Simple cleanup (just remove directory)

## Implementation Steps

### Phase 1: Update Core Abstractions

#### 1.1 Make IWorkingDirectoryProvider Session-Aware
**File**: `src/CodePunk.Core/Git/IWorkingDirectoryProvider.cs`

```csharp
public interface IWorkingDirectoryProvider
{
    string GetWorkingDirectory();
    void SetWorkingDirectory(string path);  // NEW: Allow runtime override
}
```

**File**: `src/CodePunk.Core/Git/DefaultWorkingDirectoryProvider.cs`

```csharp
public class DefaultWorkingDirectoryProvider : IWorkingDirectoryProvider
{
    private string? _overridePath;

    public string GetWorkingDirectory()
        => _overridePath ?? Environment.CurrentDirectory;

    public void SetWorkingDirectory(string path)
        => _overridePath = path;
}
```

**Change**: Registration from Singleton → Scoped
**File**: `src/CodePunk.Infrastructure/Configuration/ServiceCollectionExtensions.cs`

```csharp
// OLD: services.AddSingleton<IWorkingDirectoryProvider, DefaultWorkingDirectoryProvider>();
// NEW: services.AddScoped<IWorkingDirectoryProvider, DefaultWorkingDirectoryProvider>();
```

#### 1.2 Update GitSessionState to Track Worktree Path
**File**: `src/CodePunk.Core/GitSession/GitSessionState.cs`

```csharp
public record GitSessionState
{
    public string SessionId { get; init; }
    public string ShadowBranch { get; init; }
    public string OriginalBranch { get; init; }
    public string WorktreePath { get; init; }  // NEW
    public string? StashId { get; init; }
    // ... rest of properties

    public static GitSessionState Create(
        string shadowBranch,
        string originalBranch,
        string worktreePath,  // NEW
        string? stashId = null)
    {
        return new GitSessionState
        {
            SessionId = Guid.NewGuid().ToString("N"),
            ShadowBranch = shadowBranch,
            OriginalBranch = originalBranch,
            WorktreePath = worktreePath,  // NEW
            // ...
        };
    }
}
```

### Phase 2: Update GitSessionService

#### 2.1 BeginSessionAsync - Create Worktree
**File**: `src/CodePunk.Core/GitSession/GitSessionService.cs`

```csharp
public async Task<GitSessionState?> BeginSessionAsync(CancellationToken cancellationToken = default)
{
    if (!_options.Enabled) return null;

    var isRepoResult = await _gitExecutor.IsGitRepositoryAsync(cancellationToken);
    if (!isRepoResult.Success || !isRepoResult.Value) return null;

    // Get original branch
    var originalBranchResult = await _gitExecutor.GetCurrentBranchAsync(cancellationToken);
    if (!originalBranchResult.Success) return null;
    var originalBranch = originalBranchResult.Value!;

    // Generate unique IDs
    var sessionId = Guid.NewGuid().ToString("N");
    var shadowBranch = $"{_options.BranchPrefix}-{sessionId[..8]}";

    // Create worktree in temp directory
    var worktreePath = Path.Combine(
        Path.GetTempPath(),
        "codepunk-sessions",
        sessionId);

    Directory.CreateDirectory(Path.GetDirectoryName(worktreePath)!);

    var createWorktreeResult = await _gitExecutor.ExecuteAsync(
        $"worktree add \"{worktreePath}\" -b {shadowBranch}",
        cancellationToken);

    if (!createWorktreeResult.Success)
    {
        _logger.LogError("Failed to create worktree: {Error}", createWorktreeResult.Error);
        return null;
    }

    // Update working directory provider to point to worktree
    _workingDirProvider.SetWorkingDirectory(worktreePath);

    // Create session state
    var session = GitSessionState.Create(shadowBranch, originalBranch, worktreePath);
    await _stateStore.SaveAsync(session, cancellationToken);

    _currentSession = session;
    _logger.LogInformation("Started git session {SessionId} in worktree {WorktreePath}",
        session.SessionId, worktreePath);

    return session;
}
```

#### 2.2 CommitToolCallAsync - Use Worktree Path
**File**: `src/CodePunk.Core/GitSession/GitSessionService.cs`

```csharp
public async Task<bool> CommitToolCallAsync(
    string toolName,
    string summary,
    CancellationToken cancellationToken = default)
{
    if (_currentSession == null) return false;

    try
    {
        // Execute git commands in worktree directory
        var workingDir = _currentSession.WorktreePath;

        var stageResult = await _gitExecutor.ExecuteAsync(
            "add -A",
            cancellationToken,
            workingDirectory: workingDir);  // Explicit working dir

        if (!stageResult.Success)
        {
            _logger.LogWarning("Failed to stage changes: {Error}", stageResult.Error);
            return false;
        }

        var hasChangesResult = await _gitExecutor.HasUncommittedChangesAsync(
            cancellationToken,
            workingDirectory: workingDir);

        if (!hasChangesResult.Success || !hasChangesResult.Value)
        {
            _logger.LogInformation("No changes to commit for tool {ToolName}", toolName);
            return true;
        }

        var commitMessage = $"AI Tool: {toolName} - {summary}";
        var commitResult = await _gitExecutor.ExecuteAsync(
            $"commit -m \"{commitMessage.Replace("\"", "\\\"")}\"",
            cancellationToken,
            workingDirectory: workingDir);

        if (!commitResult.Success)
        {
            _logger.LogWarning("Failed to commit changes: {Error}", commitResult.Error);
            return false;
        }

        // Get commit hash and update session
        var commitHashResult = await _gitExecutor.ExecuteAsync(
            "rev-parse HEAD",
            cancellationToken,
            workingDirectory: workingDir);

        // ... rest of implementation
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error committing tool call {ToolName}", toolName);
        return false;
    }
}
```

#### 2.3 AcceptSessionAsync - Apply Patch to User Workspace
**File**: `src/CodePunk.Core/GitSession/GitSessionService.cs`

```csharp
public async Task<bool> AcceptSessionAsync(CancellationToken cancellationToken = default)
{
    if (_currentSession == null)
    {
        _logger.LogWarning("No active session to accept");
        return false;
    }

    try
    {
        var worktreePath = _currentSession.WorktreePath;
        var originalDir = _workingDirProvider.GetOriginalDirectory(); // Need to track this

        _logger.LogInformation("Accepting session {SessionId}, applying changes from worktree",
            _currentSession.SessionId);

        // Stage all changes in worktree
        var stageResult = await _gitExecutor.ExecuteAsync(
            "add -A",
            cancellationToken,
            workingDirectory: worktreePath);

        if (!stageResult.Success)
        {
            _logger.LogError("Failed to stage changes in worktree: {Error}", stageResult.Error);
            return false;
        }

        // Create a patch from the worktree
        var patchResult = await _gitExecutor.ExecuteAsync(
            "diff --cached --binary",
            cancellationToken,
            workingDirectory: worktreePath);

        if (!patchResult.Success)
        {
            _logger.LogError("Failed to create patch: {Error}", patchResult.Error);
            return false;
        }

        // Apply patch to user's workspace (if there are changes)
        if (!string.IsNullOrWhiteSpace(patchResult.Output))
        {
            // Write patch to temp file
            var patchFile = Path.Combine(Path.GetTempPath(), $"codepunk-patch-{_currentSession.SessionId}.patch");
            await File.WriteAllTextAsync(patchFile, patchResult.Output, cancellationToken);

            try
            {
                var applyResult = await _gitExecutor.ExecuteAsync(
                    $"apply \"{patchFile}\"",
                    cancellationToken,
                    workingDirectory: originalDir);

                if (!applyResult.Success)
                {
                    _logger.LogError("Failed to apply patch to user workspace: {Error}", applyResult.Error);
                    return false;
                }
            }
            finally
            {
                File.Delete(patchFile);
            }
        }

        // Remove worktree
        var removeWorktreeResult = await _gitExecutor.ExecuteAsync(
            $"worktree remove \"{worktreePath}\" --force",
            cancellationToken,
            workingDirectory: originalDir);

        if (!removeWorktreeResult.Success)
        {
            _logger.LogWarning("Failed to remove worktree (will cleanup manually): {Error}",
                removeWorktreeResult.Error);

            // Manual cleanup
            if (Directory.Exists(worktreePath))
            {
                Directory.Delete(worktreePath, recursive: true);
            }
        }

        // Delete shadow branch
        await _gitExecutor.ExecuteAsync(
            $"branch -D {_currentSession.ShadowBranch}",
            cancellationToken,
            workingDirectory: originalDir);

        // Cleanup session state
        _currentSession = _currentSession.MarkAccepted();
        await _stateStore.SaveAsync(_currentSession, cancellationToken);
        await _stateStore.DeleteAsync(_currentSession.SessionId, cancellationToken);

        _logger.LogInformation("Successfully accepted session {SessionId}", _currentSession.SessionId);

        // Reset working directory provider
        _workingDirProvider.SetWorkingDirectory(originalDir);
        _currentSession = null;

        return true;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error accepting session {SessionId}", _currentSession?.SessionId);
        return false;
    }
}
```

#### 2.4 RejectSessionAsync - Remove Worktree
**File**: `src/CodePunk.Core/GitSession/GitSessionService.cs`

```csharp
public async Task<bool> RejectSessionAsync(CancellationToken cancellationToken = default)
{
    if (_currentSession == null)
    {
        _logger.LogWarning("No active session to reject");
        return false;
    }

    _currentSession = _currentSession.MarkRejected();
    await RevertSessionInternalAsync(_currentSession, "User rejected", cancellationToken);
    return true;
}

private async Task RevertSessionInternalAsync(
    GitSessionState session,
    string reason,
    CancellationToken cancellationToken)
{
    try
    {
        _logger.LogInformation("Reverting session {SessionId}, reason: {Reason}",
            session.SessionId, reason);

        var originalDir = _workingDirProvider.GetOriginalDirectory();

        // Remove worktree
        var removeWorktreeResult = await _gitExecutor.ExecuteAsync(
            $"worktree remove \"{session.WorktreePath}\" --force",
            cancellationToken,
            workingDirectory: originalDir);

        if (!removeWorktreeResult.Success)
        {
            _logger.LogWarning("Failed to remove worktree: {Error}", removeWorktreeResult.Error);

            // Manual cleanup
            if (Directory.Exists(session.WorktreePath))
            {
                Directory.Delete(session.WorktreePath, recursive: true);
            }
        }

        // Delete shadow branch (unless keeping failed sessions)
        if (!(_options.KeepFailedSessionBranches && session.IsFailed))
        {
            await _gitExecutor.ExecuteAsync(
                $"branch -D {session.ShadowBranch}",
                cancellationToken,
                workingDirectory: originalDir);
        }

        await _stateStore.DeleteAsync(session.SessionId, cancellationToken);

        _logger.LogInformation("Successfully reverted session {SessionId}: {Reason}",
            session.SessionId, reason);

        if (_currentSession?.SessionId == session.SessionId)
        {
            _workingDirProvider.SetWorkingDirectory(originalDir);
            _currentSession = null;
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error reverting session {SessionId}", session.SessionId);
    }
}
```

#### 2.5 CleanupOrphanedSessionsAsync - Handle Worktrees
**File**: `src/CodePunk.Core/GitSession/GitSessionService.cs`

```csharp
public async Task CleanupOrphanedSessionsAsync(CancellationToken cancellationToken = default)
{
    if (!_options.Enabled) return;

    try
    {
        var isRepoResult = await _gitExecutor.IsGitRepositoryAsync(cancellationToken);
        if (!isRepoResult.Success || !isRepoResult.Value) return;

        // List all worktrees
        var worktreesResult = await _gitExecutor.ExecuteAsync(
            "worktree list --porcelain",
            cancellationToken);

        if (!worktreesResult.Success) return;

        // Parse worktree list to find CodePunk session worktrees
        var worktrees = ParseWorktreeList(worktreesResult.Output);
        var sessionWorktrees = worktrees
            .Where(w => w.Path.Contains("codepunk-sessions"))
            .ToList();

        // Load all session states
        var allSessions = await _stateStore.LoadAllAsync(cancellationToken);
        var sessionPaths = allSessions.Select(s => s.WorktreePath).ToHashSet();

        // Remove orphaned worktrees (no matching session state)
        foreach (var worktree in sessionWorktrees)
        {
            if (!sessionPaths.Contains(worktree.Path))
            {
                _logger.LogWarning("Found orphaned worktree: {Path}", worktree.Path);

                await _gitExecutor.ExecuteAsync(
                    $"worktree remove \"{worktree.Path}\" --force",
                    cancellationToken);

                if (Directory.Exists(worktree.Path))
                {
                    Directory.Delete(worktree.Path, recursive: true);
                }

                // Delete associated shadow branch if it exists
                if (!string.IsNullOrEmpty(worktree.Branch))
                {
                    await _gitExecutor.ExecuteAsync(
                        $"branch -D {worktree.Branch}",
                        cancellationToken);
                }
            }
        }

        // Clean up session states with missing worktrees
        foreach (var session in allSessions)
        {
            if (!Directory.Exists(session.WorktreePath))
            {
                _logger.LogInformation("Cleaning up session state for missing worktree: {SessionId}",
                    session.SessionId);
                await _stateStore.DeleteAsync(session.SessionId, cancellationToken);
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error during orphaned worktree cleanup");
    }
}

private List<WorktreeInfo> ParseWorktreeList(string output)
{
    // Parse git worktree list --porcelain format
    // Example:
    // worktree /Users/neil/Repo/CodePunk
    // HEAD abc123...
    // branch refs/heads/main
    //
    // worktree /tmp/codepunk-sessions/xyz789
    // HEAD def456...
    // branch refs/heads/ai/session-xyz789

    var worktrees = new List<WorktreeInfo>();
    var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    WorktreeInfo? current = null;
    foreach (var line in lines)
    {
        if (line.StartsWith("worktree "))
        {
            if (current != null) worktrees.Add(current);
            current = new WorktreeInfo { Path = line.Substring(9) };
        }
        else if (line.StartsWith("branch ") && current != null)
        {
            current.Branch = line.Substring(7).Replace("refs/heads/", "");
        }
    }

    if (current != null) worktrees.Add(current);
    return worktrees;
}

private record WorktreeInfo
{
    public string Path { get; init; } = "";
    public string? Branch { get; set; }
}
```

### Phase 3: Update GitCommandExecutor

#### 3.1 Support Working Directory Per Command
**File**: `src/CodePunk.Core/Git/GitCommandExecutor.cs`

```csharp
public async Task<GitResult> ExecuteAsync(
    string command,
    CancellationToken cancellationToken = default,
    string? workingDirectory = null)  // NEW: Optional override
{
    var workDir = workingDirectory ?? _workingDirProvider.GetWorkingDirectory();

    var startInfo = new ProcessStartInfo
    {
        FileName = "git",
        Arguments = command,
        WorkingDirectory = workDir,  // Use provided or default
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    // ... rest of implementation
}
```

### Phase 4: Testing

#### 4.1 Update Existing Tests
**File**: `tests/CodePunk.ComponentTests/GitSessionBehaviorTests.cs`

Update all tests to expect worktree behavior:
- Verify worktree created in temp directory
- Verify user workspace never changes
- Verify patch application on accept
- Verify worktree cleanup

#### 4.2 New Worktree-Specific Tests

```csharp
[Fact]
public async Task BeginSession_CreatesWorktreeInTempDirectory()
{
    var session = await _sessionService.BeginSessionAsync();

    session.Should().NotBeNull();
    session!.WorktreePath.Should().StartWith(Path.GetTempPath());
    Directory.Exists(session.WorktreePath).Should().BeTrue();

    // Verify user workspace unchanged
    var currentBranch = await _gitExecutor.GetCurrentBranchAsync();
    currentBranch.Value.Should().Be("main");
}

[Fact]
public async Task AcceptSession_AppliesChangesToUserWorkspace_LeavesUncommitted()
{
    var session = await _sessionService.BeginSessionAsync();

    // Create file in worktree
    var testFile = Path.Combine(session!.WorktreePath, "test.txt");
    await File.WriteAllTextAsync(testFile, "content");
    await _sessionService.CommitToolCallAsync("write_file", "Create test.txt");

    // Accept
    await _sessionService.AcceptSessionAsync();

    // Verify file exists in user workspace as uncommitted change
    var userFile = Path.Combine(_testWorkspace, "test.txt");
    File.Exists(userFile).Should().BeTrue();
    (await File.ReadAllTextAsync(userFile)).Should().Be("content");

    // Verify unstaged
    var statusResult = await _gitExecutor.ExecuteAsync("status --porcelain");
    statusResult.Output.Should().Contain("test.txt");

    // Verify worktree cleaned up
    Directory.Exists(session.WorktreePath).Should().BeFalse();
}

[Fact]
public async Task RejectSession_LeavesUserWorkspaceUntouched()
{
    // Create initial file in user workspace
    var userFile = Path.Combine(_testWorkspace, "user-file.txt");
    await File.WriteAllTextAsync(userFile, "original");

    var session = await _sessionService.BeginSessionAsync();

    // Create file in worktree
    var worktreeFile = Path.Combine(session!.WorktreePath, "ai-file.txt");
    await File.WriteAllTextAsync(worktreeFile, "ai content");
    await _sessionService.CommitToolCallAsync("write_file", "Create ai-file.txt");

    // Reject
    await _sessionService.RejectSessionAsync();

    // Verify user file unchanged
    File.Exists(userFile).Should().BeTrue();
    (await File.ReadAllTextAsync(userFile)).Should().Be("original");

    // Verify AI file never appeared
    var aiFileInUserWorkspace = Path.Combine(_testWorkspace, "ai-file.txt");
    File.Exists(aiFileInUserWorkspace).Should().BeFalse();

    // Verify worktree cleaned up
    Directory.Exists(session.WorktreePath).Should().BeFalse();
}

[Fact]
public async Task CleanupOrphanedSessions_RemovesOrphanedWorktrees()
{
    // Create orphaned worktree manually
    var orphanedPath = Path.Combine(Path.GetTempPath(), "codepunk-sessions", "orphan-123");
    await _gitExecutor.ExecuteAsync($"worktree add \"{orphanedPath}\" -b ai/session-orphan");

    Directory.Exists(orphanedPath).Should().BeTrue();

    // Run cleanup
    await _sessionService.CleanupOrphanedSessionsAsync();

    // Verify cleanup
    Directory.Exists(orphanedPath).Should().BeFalse();

    var branchesResult = await _gitExecutor.ExecuteAsync("branch");
    branchesResult.Output.Should().NotContain("ai/session-orphan");
}
```

### Phase 5: Migration & Deployment

#### 5.1 Breaking Changes
- Session state format changes (adds `WorktreePath`)
- Any active sessions will be orphaned during upgrade

#### 5.2 Migration Strategy

**Option 1: Clean slate**
- Reject all active sessions on startup before migration
- Simplest, acceptable for alpha

**Option 2: State migration**
- Detect old format session states
- Auto-reject them with warning
- Log migration event

```csharp
// In GitSessionStateStore
public async Task<GitSessionState?> LoadAsync(string sessionId, ...)
{
    var json = await File.ReadAllTextAsync(statePath);
    var session = JsonSerializer.Deserialize<GitSessionState>(json);

    // Detect old format (missing WorktreePath)
    if (session != null && string.IsNullOrEmpty(session.WorktreePath))
    {
        _logger.LogWarning("Found old-format session {SessionId}, will be cleaned up", sessionId);
        return null;  // Treat as orphaned
    }

    return session;
}
```

#### 5.3 Rollout Plan
1. Merge worktree implementation
2. Update version to 0.2.0-alpha.1 (breaking change)
3. Document in changelog:
   - Breaking: Active sessions will be lost on upgrade
   - Recommendation: Accept or reject active sessions before upgrading
4. Monitor for issues with worktree on different platforms

## Edge Cases & Considerations

### Disk Space
- Each session duplicates workspace
- Typical project: 100-500MB
- Max sessions: 10-20 before disk pressure
- **Mitigation**: Session timeout cleanup, max concurrent sessions config

### Performance
- Worktree creation: ~1-3 seconds for large repos
- Acceptable for interactive workflow
- **Optimization**: Could reuse worktrees between sessions

### Cross-Platform
- Git worktree available since Git 2.5 (2015)
- Windows, Mac, Linux all supported
- Path quoting already handled in ExecuteAsync

### Network Filesystems
- Worktrees may be slower on NFS/SMB
- **Mitigation**: Allow WorktreeBasePath config option
  - Default: `/tmp/codepunk-sessions`
  - Override: Could use local disk on dev machines

### Concurrent Sessions
- Architecture now supports it!
- Each session has isolated worktree
- Future feature: Allow multiple parallel AI sessions

## Configuration Updates

**File**: `src/CodePunk.Core/GitSession/GitSessionOptions.cs`

```csharp
public class GitSessionOptions
{
    public bool Enabled { get; set; } = true;
    public bool AutoStartSession { get; set; } = true;
    public string BranchPrefix { get; set; } = "ai/session";
    public string WorktreeBasePath { get; set; } = "";  // NEW: Empty = use system temp
    public int SessionTimeoutMinutes { get; set; } = 30;
    public bool AutoRevertOnTimeout { get; set; } = true;
    public bool KeepFailedSessionBranches { get; set; } = false;
    public string StateStorePath { get; set; } = "~/.codepunk/git-sessions";
}
```

**File**: `src/CodePunk.Console/appsettings.json`

```json
{
  "GitSession": {
    "Enabled": true,
    "AutoStartSession": true,
    "BranchPrefix": "ai/session",
    "WorktreeBasePath": "",
    "SessionTimeoutMinutes": 30,
    "AutoRevertOnTimeout": true,
    "KeepFailedSessionBranches": false,
    "StateStorePath": "~/.codepunk/git-sessions"
  }
}
```

## Files to Modify

### Core Changes
- [ ] `src/CodePunk.Core/Git/IWorkingDirectoryProvider.cs` - Add SetWorkingDirectory
- [ ] `src/CodePunk.Core/Git/DefaultWorkingDirectoryProvider.cs` - Implement override
- [ ] `src/CodePunk.Core/Git/GitCommandExecutor.cs` - Add workingDirectory parameter
- [ ] `src/CodePunk.Core/GitSession/GitSessionState.cs` - Add WorktreePath property
- [ ] `src/CodePunk.Core/GitSession/GitSessionService.cs` - Full worktree implementation
- [ ] `src/CodePunk.Core/GitSession/GitSessionOptions.cs` - Add WorktreeBasePath

### Infrastructure
- [ ] `src/CodePunk.Infrastructure/Configuration/ServiceCollectionExtensions.cs` - Scoped provider

### Tests
- [ ] `tests/CodePunk.ComponentTests/GitSessionBehaviorTests.cs` - Update all tests
- [ ] Add new worktree-specific tests

### Documentation
- [ ] Update README with worktree approach
- [ ] Add migration notes to CHANGELOG

## Testing Checklist

- [ ] Worktree created successfully
- [ ] User workspace never changes during session
- [ ] Files written by tools appear in worktree
- [ ] Accept applies changes to user workspace (unstaged)
- [ ] Reject leaves user workspace untouched
- [ ] Worktree cleaned up after accept/reject
- [ ] Orphaned worktree cleanup works
- [ ] Multi-file changes work correctly
- [ ] Binary files handled properly
- [ ] Large files (>100MB) work
- [ ] Works on macOS ✓
- [ ] Works on Linux
- [ ] Works on Windows
- [ ] Concurrent tool execution safe
- [ ] Process crash recovery verified

## Success Criteria

1. ✅ User workspace NEVER changes unexpectedly
2. ✅ No orphaned branch states on crash
3. ✅ All existing git session tests pass
4. ✅ Worktree cleanup is 100% reliable
5. ✅ Performance acceptable (<3s session creation)
6. ✅ Cross-platform compatibility verified

## Timeline Estimate

- Phase 1 (Abstractions): 2-3 hours
- Phase 2 (GitSessionService): 4-5 hours
- Phase 3 (GitCommandExecutor): 1 hour
- Phase 4 (Testing): 3-4 hours
- Phase 5 (Migration): 1-2 hours

**Total**: ~12-15 hours of focused development
