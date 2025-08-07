using CodePunk.Core.Models;

namespace CodePunk.Core.Abstractions;

/// <summary>
/// Repository interface for file history data access
/// </summary>
public interface IFileHistoryRepository
{
    Task<SessionFile?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SessionFile>> GetBySessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<SessionFile?> GetLatestVersionAsync(string sessionId, string path, CancellationToken cancellationToken = default);
    Task<SessionFile> CreateAsync(SessionFile file, CancellationToken cancellationToken = default);
    Task<SessionFile> UpdateAsync(SessionFile file, CancellationToken cancellationToken = default);
}
