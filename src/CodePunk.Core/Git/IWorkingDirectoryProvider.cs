namespace CodePunk.Core.Git;

/// <summary>
/// Provides the current working directory for git operations
/// </summary>
public interface IWorkingDirectoryProvider
{
    string GetWorkingDirectory();
}

/// <summary>
/// Default implementation that uses Environment.CurrentDirectory
/// </summary>
public class DefaultWorkingDirectoryProvider : IWorkingDirectoryProvider
{
    public string GetWorkingDirectory() => Directory.GetCurrentDirectory();
}
