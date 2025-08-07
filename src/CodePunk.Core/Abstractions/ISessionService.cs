using CodePunk.Core.Models;

namespace CodePunk.Core.Abstractions;

/// <summary>
/// Service for managing chat sessions
/// </summary>
public interface ISessionService
{
    Task<Session?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Session>> GetRecentAsync(int count = 50, CancellationToken cancellationToken = default);
    Task<Session> CreateAsync(string title, string? parentSessionId = null, CancellationToken cancellationToken = default);
    Task<Session> UpdateAsync(Session session, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
