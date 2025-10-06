namespace CodePunk.Core.Git;

/// <summary>
/// Provides the current working directory for git operations
/// </summary>
public interface IWorkingDirectoryProvider
{
    /// <summary>
    /// Gets the current working directory (may be overridden for git worktrees)
    /// </summary>
    string GetWorkingDirectory();

    /// <summary>
    /// Sets an override working directory (used for git worktree sessions)
    /// </summary>
    void SetWorkingDirectory(string path);

    /// <summary>
    /// Gets the original working directory (before any overrides)
    /// </summary>
    string GetOriginalDirectory();
}

/// <summary>
/// Default implementation that uses Environment.CurrentDirectory with optional override support
/// </summary>
public class DefaultWorkingDirectoryProvider : IWorkingDirectoryProvider
{
    private readonly string _originalDirectory;
    private string? _overrideDirectory;

    public DefaultWorkingDirectoryProvider()
    {
        _originalDirectory = Directory.GetCurrentDirectory();
    }

    public string GetWorkingDirectory()
        => _overrideDirectory ?? _originalDirectory;

    public void SetWorkingDirectory(string path)
        => _overrideDirectory = path;

    public string GetOriginalDirectory()
        => _originalDirectory;
}
