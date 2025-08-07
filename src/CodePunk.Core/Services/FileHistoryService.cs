using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using Microsoft.Extensions.Logging;

namespace CodePunk.Core.Services;

public class FileHistoryService : IFileHistoryService
{
    private readonly IFileHistoryRepository _repository;
    private readonly ILogger<FileHistoryService> _logger;

    public FileHistoryService(IFileHistoryRepository repository, ILogger<FileHistoryService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<SessionFile?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return await _repository.GetByIdAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<SessionFile>> GetBySessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return Array.Empty<SessionFile>();

        return await _repository.GetBySessionAsync(sessionId, cancellationToken);
    }

    public async Task<SessionFile?> GetLatestVersionAsync(string sessionId, string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(path))
            return null;

        return await _repository.GetLatestVersionAsync(sessionId, path, cancellationToken);
    }

    public async Task<SessionFile> CreateAsync(SessionFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        _logger.LogInformation("Creating file version {FileId} for session {SessionId} at path '{Path}'", 
            file.Id, file.SessionId, file.Path);

        return await _repository.CreateAsync(file, cancellationToken);
    }

    public async Task<SessionFile> UpdateAsync(SessionFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        _logger.LogInformation("Updating file version {FileId}", file.Id);

        return await _repository.UpdateAsync(file, cancellationToken);
    }
}
