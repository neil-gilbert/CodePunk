namespace CodePunk.Core.GitSession;

public class GitSessionOptions
{
    public bool Enabled { get; set; } = true;
    public bool AutoStartSession { get; set; } = true;
    public string BranchPrefix { get; set; } = "ai/session";
    public string WorktreeBasePath { get; set; } = "";  // Empty = use system temp directory
    public int SessionTimeoutMinutes { get; set; } = 30;
    public bool AutoRevertOnTimeout { get; set; } = true;
    public bool CleanupOrphanedSessionsOnStartup { get; set; } = true;
    public bool KeepFailedSessionBranches { get; set; } = false;
    public string StateStorePath { get; set; } = "~/.codepunk/git-sessions";

    public string GetExpandedStateStorePath()
    {
        var path = StateStorePath;
        if (path.StartsWith("~/"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = Path.Combine(home, path[2..]);
        }
        return path;
    }

    public string GetExpandedWorktreeBasePath()
    {
        if (string.IsNullOrEmpty(WorktreeBasePath))
        {
            return Path.Combine(Path.GetTempPath(), "codepunk-sessions");
        }

        var path = WorktreeBasePath;
        if (path.StartsWith("~/"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = Path.Combine(home, path[2..]);
        }
        return path;
    }
}
