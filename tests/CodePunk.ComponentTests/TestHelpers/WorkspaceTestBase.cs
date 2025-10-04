namespace CodePunk.ComponentTests.TestHelpers;

/// <summary>
/// Base class for tests that need an isolated workspace directory
/// Handles Environment.CurrentDirectory management and cleanup
/// </summary>
public abstract class WorkspaceTestBase : IDisposable
{
    protected readonly string TestWorkspace;
    private readonly string _originalDirectory;

    protected WorkspaceTestBase(string testPrefix)
    {
        _originalDirectory = Environment.CurrentDirectory;
        TestWorkspace = Path.Combine(Path.GetTempPath(), $"codepunk_{testPrefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(TestWorkspace);
        Environment.CurrentDirectory = TestWorkspace;
    }

    public virtual void Dispose()
    {
        try { Environment.CurrentDirectory = _originalDirectory; } catch { }
        if (Directory.Exists(TestWorkspace))
        {
            try
            {
                Directory.Delete(TestWorkspace, recursive: true);
            }
            catch { }
        }
    }
}
