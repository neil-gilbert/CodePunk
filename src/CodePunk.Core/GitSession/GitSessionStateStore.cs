using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodePunk.Core.GitSession;

public interface IGitSessionStateStore
{
    Task SaveAsync(GitSessionState state, CancellationToken cancellationToken = default);
    Task<GitSessionState?> LoadAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<List<GitSessionState>> LoadAllAsync(CancellationToken cancellationToken = default);
    Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default);
}

public class GitSessionStateStore : IGitSessionStateStore
{
    private readonly GitSessionOptions _options;
    private readonly ILogger<GitSessionStateStore> _logger;
    private readonly string _storeDirectory;

    public GitSessionStateStore(
        IOptions<GitSessionOptions> options,
        ILogger<GitSessionStateStore> logger)
    {
        _options = options.Value;
        _logger = logger;
        _storeDirectory = _options.GetExpandedStateStorePath();
        Directory.CreateDirectory(_storeDirectory);
    }

    public async Task SaveAsync(GitSessionState state, CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(_storeDirectory);

            var filePath = GetFilePath(state.SessionId);
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(filePath, json, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save session state: {SessionId}", state.SessionId);
            throw;
        }
    }

    public async Task<GitSessionState?> LoadAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var filePath = GetFilePath(sessionId);
            if (!File.Exists(filePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            return JsonSerializer.Deserialize<GitSessionState>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load session state: {SessionId}", sessionId);
            return null;
        }
    }

    public async Task<List<GitSessionState>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var sessions = new List<GitSessionState>();
            var files = Directory.GetFiles(_storeDirectory, "*.json");

            foreach (var file in files)
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var state = JsonSerializer.Deserialize<GitSessionState>(json);
                if (state != null)
                {
                    sessions.Add(state);
                }
            }

            return sessions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load all session states");
            return new List<GitSessionState>();
        }
    }

    public Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var filePath = GetFilePath(sessionId);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete session state: {SessionId}", sessionId);
            throw;
        }
    }

    private string GetFilePath(string sessionId)
    {
        return Path.Combine(_storeDirectory, $"{sessionId}.json");
    }
}
