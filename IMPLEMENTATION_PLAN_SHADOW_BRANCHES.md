# Implementation Plan: Git Shadow Branch System

## Overview
Implement a Git-based shadow branch system to manage AI-generated code changes with granular history and clean user commits.

## Architecture

### Core Components

#### 1. Git Operations Layer (Adapters)
**Purpose:** Shell-based Git command execution with zero external dependencies

**Files:**
- `src/CodePunk.Core/Git/GitCommandExecutor.cs` - Process-based git command runner
- `src/CodePunk.Core/Git/GitOperationResult.cs` - Result type for git operations
- `src/CodePunk.Core/Git/IGitCommandExecutor.cs` - Abstraction for git operations

**Responsibilities:**
- Execute git commands via Process
- Parse git output (status, branch names, commit hashes)
- Handle errors and exit codes
- No LibGit2Sharp dependency

#### 2. Session Management (Domain Service)
**Purpose:** Manage AI session lifecycle with shadow branches

**Files:**
- `src/CodePunk.Core/GitSession/GitSessionService.cs` - Core session logic
- `src/CodePunk.Core/GitSession/IGitSessionService.cs` - Service abstraction
- `src/CodePunk.Core/GitSession/GitSessionState.cs` - Session state model
- `src/CodePunk.Core/GitSession/GitSessionOptions.cs` - Configuration options
- `src/CodePunk.Core/GitSession/GitSessionStateStore.cs` - Persist sessions to disk
- `src/CodePunk.Core/GitSession/GitSessionCleanupService.cs` - Startup cleanup of orphaned sessions

**Responsibilities:**
- Create shadow branches (`ai/session-{guid}`)
- Track original branch and stashed changes
- Record tool call commits
- Squash merge on accept
- Delete shadow branch on reject/accept
- Auto-revert on session end without explicit accept
- Handle git conflicts
- Cleanup orphaned sessions on startup

**State Tracking:**
```csharp
public class GitSessionState
{
    public string SessionId { get; init; }
    public string ShadowBranch { get; init; }
    public string OriginalBranch { get; init; }
    public string? StashId { get; init; }
    public List<GitToolCallCommit> ToolCallCommits { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? AcceptedAt { get; init; }
    public DateTimeOffset? RejectedAt { get; init; }
    public DateTimeOffset LastActivityAt { get; init; }
    public bool IsFailed { get; init; }
    public string? FailureReason { get; init; }
}

public class GitToolCallCommit
{
    public string ToolName { get; init; }
    public string CommitHash { get; init; }
    public DateTimeOffset CommittedAt { get; init; }
    public List<string> FilesChanged { get; init; }
}
```

#### 3. Tool Integration Hook
**Purpose:** Intercept tool execution to create commits

**Files:**
- `src/CodePunk.Core/GitSession/GitSessionToolInterceptor.cs` - Tool execution wrapper
- `src/CodePunk.Core/Abstractions/IToolExecutionInterceptor.cs` - Hook interface

**Responsibilities:**
- Wrap tool execution in try-catch-finally
- Commit changes after each successful tool call
- Link commits to tool invocations
- Skip commits for read-only tools
- Mark session as failed on unhandled exceptions
- Ensure session state always updated (LastActivityAt)

**Exception Safety Pattern:**
```csharp
public async Task<ToolResult> ExecuteAsync(ITool tool, JsonElement args)
{
    try
    {
        var result = await tool.ExecuteAsync(args);

        if (result.IsSuccess && IsWriteTool(tool))
        {
            await _gitSession.CommitToolCallAsync(tool.Name, result);
        }

        await _gitSession.UpdateActivityAsync();
        return result;
    }
    catch (Exception ex)
    {
        await _gitSession.MarkAsFailedAsync(ex.Message);
        throw;
    }
}
```

#### 4. Startup Cleanup (Background Service)
**Purpose:** Detect and cleanup orphaned sessions on application start

**Files:**
- `src/CodePunk.Core/GitSession/GitSessionCleanupService.cs` - Background cleanup service
- `src/CodePunk.Core/GitSession/IHostedService` - .NET hosted service interface

**Responsibilities:**
- Scan `~/.codepunk/git-sessions/` for session state files on startup
- Check each session with `ShouldAutoRevert()` logic
- Auto-revert sessions without `AcceptedAt` timestamp
- Delete shadow branches for orphaned sessions
- Restore stashed changes if possible
- Log cleanup summary to console
- Register shutdown handler for graceful cleanup

**Startup Flow:**
```csharp
public async Task StartAsync(CancellationToken cancellationToken)
{
    var sessionFiles = Directory.GetFiles(options.StateStorePath, "*.json");

    foreach (var file in sessionFiles)
    {
        var session = await LoadSessionAsync(file);

        if (ShouldAutoRevert(session))
        {
            await RevertSessionAsync(session);
            Console.WriteLine($"Auto-reverted orphaned session: {session.SessionId}");
        }
    }
}
```

#### 5. User Commands (Ports)
**Purpose:** User-facing session control

**Files:**
- `src/CodePunk.Console/Commands/AcceptSessionCommand.cs` - Accept AI changes
- `src/CodePunk.Console/Commands/RejectSessionCommand.cs` - Discard AI changes
- `src/CodePunk.Console/Commands/SessionStatusCommand.cs` - View current session

**Commands:**
- `/accept-session` - Squash merge shadow branch
- `/reject-session` - Delete shadow branch
- `/session-status` - Show commits in current session

### Data Flow

```
User Request
    ↓
[Chat Session Starts]
    ↓
GitSessionService.BeginSessionAsync()
    ↓
- Record original branch (git branch --show-current)
- Stash uncommitted changes (git stash push -u)
- Create shadow branch (git checkout -b ai/session-{guid})
    ↓
[AI Tool Execution Loop]
    ↓
For each tool call:
    ToolExecutionInterceptor.ExecuteAsync()
        ↓
    - Execute tool (write_file, replace_in_file, etc.)
    - Stage changes (git add -A)
    - Commit (git commit -m "AI Tool: {tool_name} - {summary}")
    - Record commit hash + tool metadata
    ↓
[Session Complete]
    ↓
User chooses: Accept, Reject, or (session ends without choice)
    ↓
If Accept:
    - Checkout original branch (git checkout {original})
    - Squash merge (git merge --squash ai/session-{guid})
    - Commit (git commit -m "{user_request_summary}")
    - Delete shadow branch (git branch -D ai/session-{guid})
    - Re-apply stash if exists (git stash pop)
    ↓
If Reject OR Session Ends Without Accept:
    - Checkout original branch (git checkout {original})
    - Delete shadow branch (git branch -D ai/session-{guid})
    - Re-apply stash if exists (git stash pop)
    - Log reason (explicit reject vs auto-revert)
```

## Testing Strategy

### Component Tests (Primary)
Test at the outermost boundaries - user behavior and file system effects.

**Test File:** `tests/CodePunk.ComponentTests/GitSessionBehaviorTests.cs`

**Test Scenarios:**
1. **Session Creation and Accept**
   - Given: Clean git repo on main branch
   - When: User starts session, AI modifies 3 files, user accepts
   - Then: Main branch has 1 commit with all changes, shadow branch deleted

2. **Session Rejection**
   - Given: Session with 5 tool commits
   - When: User rejects session
   - Then: All changes discarded, original branch unchanged, shadow branch deleted

3. **Uncommitted Changes Preservation**
   - Given: User has uncommitted changes
   - When: Session starts and completes
   - Then: Uncommitted changes restored after session

4. **Multiple Tool Calls**
   - Given: Session with write_file, replace_in_file, shell command
   - When: Each tool succeeds
   - Then: Shadow branch has 3 commits, squash creates 1 commit on main

5. **Tool Failure Mid-Session**
   - Given: Session with 2 successful tools, 1 failed tool
   - When: User accepts
   - Then: Only successful changes committed

6. **Session Without Git Repository**
   - Given: Workspace is not a git repo
   - When: Session starts
   - Then: Session disabled, tools execute normally without commits

7. **Conflict Handling**
   - Given: Original branch modified during session
   - When: User accepts (merge conflict occurs)
   - Then: User informed of conflict, manual resolution required

8. **Unhandled Exception During Session**
   - Given: Session with 2 commits
   - When: Unhandled exception occurs during tool execution
   - Then: Session auto-reverts, shadow branch deleted, original branch restored

9. **Application Shutdown Without Accept**
   - Given: Active session with changes
   - When: Application exits without user accepting/rejecting
   - Then: On next startup, detect orphaned session and auto-cleanup

10. **Session Ends Without User Decision**
    - Given: Session completes successfully
    - When: User never calls /accept-session or /reject-session
    - Then: Session auto-reverts after timeout or new session starts

11. **Tool Throws Exception**
    - Given: Session in progress
    - When: Tool execution throws exception (not caught by tool)
    - Then: Exception logged, no commit created, session remains active for other tools

12. **Git Command Fails During Commit**
    - Given: Session trying to commit tool changes
    - When: Git command fails (disk full, permissions, etc.)
    - Then: Error logged, session marked as failed, auto-revert on cleanup

**Test File:** `tests/CodePunk.ComponentTests/GitCommandExecutorTests.cs`

**Test Scenarios:**
1. **Execute Git Command Success**
   - When: Execute valid git command
   - Then: Return success with output

2. **Execute Git Command Failure**
   - When: Execute invalid git command
   - Then: Return failure with error message

3. **Parse Git Status**
   - Given: Modified, staged, and untracked files
   - When: Parse git status output
   - Then: Correctly categorize files

4. **Parse Git Branch Name**
   - When: Get current branch
   - Then: Return branch name

### Integration Tests
Test service interactions without file system or git operations.

**Test File:** `tests/CodePunk.Integration.Tests/GitSessionServiceIntegrationTests.cs`

**Test Scenarios:**
1. Service correctly uses GitCommandExecutor
2. State transitions (started → active → accepted/rejected)
3. Error handling with mocked git failures

## Implementation Steps

### Phase 1: Git Operations Foundation
1. Create `GitCommandExecutor` with Process-based git execution
2. Create `GitOperationResult` result type
3. Create unit-style tests for command parsing
4. Test on actual git repos (component tests)

### Phase 2: Session Service
1. Create `GitSessionService` with session lifecycle
2. Create `GitSessionStateStore` for persisting session state
3. Implement `BeginSessionAsync` (stash, create branch, persist state)
4. Implement `CommitToolCallAsync` (stage, commit, record, update state)
5. Implement `AcceptSessionAsync` (checkout, squash, commit, delete, mark accepted)
6. Implement `RejectSessionAsync` (checkout, delete, mark rejected)
7. Update state on every operation (LastActivityAt tracking)
8. Add component tests for full workflows

### Phase 3: Tool Integration
1. Create `IToolExecutionInterceptor` abstraction
2. Implement `GitSessionToolInterceptor`
3. Register interceptor in DI pipeline
4. Exclude read-only tools from commits
5. Add tests for tool interception

### Phase 4: User Commands
1. Implement `/accept-session` command
2. Implement `/reject-session` command
3. Implement `/session-status` command
4. Add command tests

### Phase 5: Auto-Revert and Cleanup
1. Implement `GitSessionCleanupService` for startup orphan detection
2. Add orphaned session scanning on application start
3. Register application shutdown hooks for active session cleanup
4. Implement session timeout checking
5. Add try-finally blocks in tool interceptor for exception safety
6. Handle new session starting with active session (auto-revert previous)
7. Add component tests for all auto-revert scenarios (tests 8-12)
8. Add logging for all revert operations

### Phase 6: Configuration & Polish
1. Add `GitSessionOptions` to appsettings.json (include timeout settings)
2. Add auto-session mode vs manual mode
3. Add environment variables for debugging (CODEPUNK_KEEP_FAILED_SESSIONS)
4. Handle edge cases (detached HEAD, merge conflicts)
5. Add user notifications for auto-reverts
6. Performance testing with large sessions

## Configuration

**appsettings.json:**
```json
{
  "GitSession": {
    "Enabled": true,
    "AutoStartSession": true,
    "BranchPrefix": "ai/session",
    "StashUncommittedChanges": true,
    "DefaultCommitMessageTemplate": "AI Session: {summary}",
    "SessionTimeoutMinutes": 30,
    "AutoRevertOnTimeout": true,
    "CleanupOrphanedSessionsOnStartup": true,
    "KeepFailedSessionBranches": false,
    "StateStorePath": "~/.codepunk/git-sessions"
  }
}
```

## Error Handling

### Default to Safe Behavior
**Principle:** Changes are only committed to the user's branch with EXPLICIT acceptance. Any other outcome results in auto-revert.

**Auto-Revert Triggers:**
1. **Unhandled Exception** - Any exception during session that escapes to application boundary
2. **Application Shutdown** - Process exits, crashes, or is killed
3. **Session Timeout** - Session inactive for configured duration (default: 30 minutes)
4. **New Session Starts** - User starts new session without accepting/rejecting previous
5. **Explicit Rejection** - User calls `/reject-session`

**Implementation:**
- Session state persisted to disk (`~/.codepunk/git-sessions/{session-id}.json`)
- On startup, scan for orphaned sessions and auto-revert
- Register application shutdown hook to cleanup active sessions
- Wrap tool execution in try-catch with session cleanup in finally block
- Session tracks `AcceptedAt` timestamp - null means not accepted, triggers auto-revert

**Auto-Revert Decision Logic:**
```csharp
bool ShouldAutoRevert(GitSessionState session)
{
    // Already accepted - don't revert
    if (session.AcceptedAt.HasValue) return false;

    // Explicitly rejected - revert
    if (session.RejectedAt.HasValue) return true;

    // Failed during execution - revert
    if (session.IsFailed) return true;

    // Session timeout - revert
    var timeout = TimeSpan.FromMinutes(options.SessionTimeoutMinutes);
    if (DateTime.UtcNow - session.LastActivityAt > timeout) return true;

    // Orphaned (process exited) - revert
    if (IsOrphanedSession(session)) return true;

    return false;
}
```

### Expected Errors
- Not a git repository → Disable feature, continue normally
- Detached HEAD → Warn user, prevent session start
- Merge conflicts → Guide user to resolve, provide conflict details
- Git command failures → Detailed error messages, safe rollback
- Orphaned sessions on startup → Auto-cleanup with user notification

### Rollback Strategy
- Any failure during accept/reject → Restore original branch
- Stash always preserved until successful completion
- Shadow branch deleted on revert (can be kept with `CODEPUNK_KEEP_FAILED_SESSIONS=true`)
- Failed session metadata logged to `~/.codepunk/logs/failed-sessions.log`

## Dependencies

**New NuGet Packages:** None (using Process for git)

**Project References:**
- CodePunk.Core (core services)
- CodePunk.Console (commands)

**Testing:**
- xUnit
- FluentAssertions
- Moq (minimal use, only for external dependencies)

## Feature Flags

- `GitSession:Enabled` - Master switch
- `GitSession:AutoStartSession` - Automatic session creation per user request
- Environment variable `CODEPUNK_GIT_SESSION_DISABLED` - Override disable

## Future Enhancements (Out of Scope)

- Visual diff of session changes
- Session history browser
- Cherry-pick specific tool commits
- Multi-session support (parallel sessions)
- Integration with GitHub PR creation
