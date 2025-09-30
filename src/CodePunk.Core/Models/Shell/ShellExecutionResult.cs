namespace CodePunk.Core.Models.Shell;

public class ShellExecutionResult
{
    public string Command { get; init; } = string.Empty;
    public string Directory { get; init; } = string.Empty;
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
    public int? ExitCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool WasCancelled { get; init; }
    public List<int> BackgroundProcessIds { get; init; } = new();
}
