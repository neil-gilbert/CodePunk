using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using Microsoft.Extensions.Logging;

namespace CodePunk.Core.Services;

public class SessionService : ISessionService
{
    private readonly ISessionRepository _repository;
    private readonly ILogger<SessionService> _logger;

    public SessionService(ISessionRepository repository, ILogger<SessionService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Session?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return await _repository.GetByIdAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<Session>> GetRecentAsync(int count = 50, CancellationToken cancellationToken = default)
    {
        if (count <= 0)
            count = 50;
        if (count > 500) // Reasonable upper limit
            count = 500;

        return await _repository.GetRecentAsync(count, cancellationToken);
    }

    public async Task<Session> CreateAsync(string title, string? parentSessionId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Session title cannot be empty", nameof(title));

        var session = Session.Create(title, parentSessionId);
        
        _logger.LogInformation("Creating new session {SessionId} with title '{Title}'", session.Id, title);
        
        return await _repository.CreateAsync(session, cancellationToken);
    }

    public async Task<Session> UpdateAsync(Session session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        
        _logger.LogInformation("Updating session {SessionId}", session.Id);
        
        return await _repository.UpdateAsync(session, cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        _logger.LogInformation("Deleting session {SessionId}", id);
        
        await _repository.DeleteAsync(id, cancellationToken);
    }
}
