using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using Microsoft.Extensions.Logging;

namespace CodePunk.Core.Services;

public class MessageService : IMessageService
{
    private readonly IMessageRepository _repository;
    private readonly ILogger<MessageService> _logger;

    public MessageService(IMessageRepository repository, ILogger<MessageService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Message?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return await _repository.GetByIdAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<Message>> GetBySessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return Array.Empty<Message>();

        return await _repository.GetBySessionAsync(sessionId, cancellationToken);
    }

    public async Task<Message> CreateAsync(Message message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _logger.LogInformation("Creating message {MessageId} for session {SessionId}", message.Id, message.SessionId);

        return await _repository.CreateAsync(message, cancellationToken);
    }

    public async Task<Message> UpdateAsync(Message message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _logger.LogInformation("Updating message {MessageId}", message.Id);

        return await _repository.UpdateAsync(message, cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        _logger.LogInformation("Deleting message {MessageId}", id);

        await _repository.DeleteAsync(id, cancellationToken);
    }

    public async Task DeleteBySessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return;

        _logger.LogInformation("Deleting all messages for session {SessionId}", sessionId);

        await _repository.DeleteBySessionAsync(sessionId, cancellationToken);
    }
}
