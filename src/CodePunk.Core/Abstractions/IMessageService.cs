using CodePunk.Core.Models;

namespace CodePunk.Core.Abstractions;

/// <summary>
/// Service for managing messages within sessions
/// </summary>
public interface IMessageService
{
    Task<Message?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Message>> GetBySessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<Message> CreateAsync(Message message, CancellationToken cancellationToken = default);
    Task<Message> UpdateAsync(Message message, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task DeleteBySessionAsync(string sessionId, CancellationToken cancellationToken = default);
}
