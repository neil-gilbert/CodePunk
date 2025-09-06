namespace CodePunk.Console.Stores;

/// <summary>
/// Lightweight file-based session transcript persistence for CLI mode (separate from DB persistence).
/// </summary>
public interface ISessionFileStore
{
    Task<string> CreateAsync(string? title, string? agent, string? model, CancellationToken ct = default);
    Task AppendMessageAsync(string sessionId, string role, string content, CancellationToken ct = default);
    Task<SessionRecord?> GetAsync(string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<SessionMetadata>> ListAsync(int? take = null, CancellationToken ct = default);
}
