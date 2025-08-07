using CodePunk.Core.Models;

namespace CodePunk.Core.Abstractions;

/// <summary>
/// Repository interface for session data access
/// </summary>
public interface ISessionRepository
{
    Task<Session?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Session>> GetRecentAsync(int count = 50, CancellationToken cancellationToken = default);
    Task<Session> CreateAsync(Session session, CancellationToken cancellationToken = default);
    Task<Session> UpdateAsync(Session session, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
