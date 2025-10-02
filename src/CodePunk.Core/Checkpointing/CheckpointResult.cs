namespace CodePunk.Core.Checkpointing;

public record CheckpointResult(
    bool Success,
    string? ErrorMessage = null,
    string? ErrorDetails = null)
{
    public static CheckpointResult Ok() => new(true);
    public static CheckpointResult Fail(string message, string? details = null) =>
        new(false, message, details);
}

public record CheckpointResult<T>(
    bool Success,
    T? Data = default,
    string? ErrorMessage = null,
    string? ErrorDetails = null)
{
    public static CheckpointResult<T> Ok(T data) => new(true, data);
    public static CheckpointResult<T> Fail(string message, string? details = null) =>
        new(false, default, message, details);
}
