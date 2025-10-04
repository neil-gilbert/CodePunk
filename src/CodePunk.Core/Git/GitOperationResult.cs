namespace CodePunk.Core.Git;

public record GitOperationResult(
    bool Success,
    string Output = "",
    string Error = "",
    int ExitCode = 0)
{
    public static GitOperationResult Succeeded(string output = "") =>
        new(true, output, "", 0);

    public static GitOperationResult Failed(string error, int exitCode = 1) =>
        new(false, "", error, exitCode);
}

public record GitOperationResult<T>(
    bool Success,
    T? Value = default,
    string Error = "",
    int ExitCode = 0)
{
    public static GitOperationResult<T> Succeeded(T value) =>
        new(true, value, "", 0);

    public static GitOperationResult<T> Failed(string error, int exitCode = 1) =>
        new(false, default, error, exitCode);
}
