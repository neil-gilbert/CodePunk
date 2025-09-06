using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodePunk.Console.Stores;

public class SessionFileStore : ISessionFileStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<string> CreateAsync(string? title, string? agent, string? model, CancellationToken ct = default)
    {
        ConfigPaths.EnsureCreated();
        var id = GenerateId();
        var meta = new SessionMetadata
        {
            Id = id,
            Title = string.IsNullOrWhiteSpace(title) ? id : title?.Trim(),
            Agent = agent,
            Model = model,
            CreatedUtc = DateTime.UtcNow,
            LastUpdatedUtc = DateTime.UtcNow
        };
        var record = new SessionRecord { Metadata = meta };
        await PersistRecordAsync(record, ct).ConfigureAwait(false);
        await UpdateIndexAsync(meta, ct).ConfigureAwait(false);
        return id;
    }

    public async Task AppendMessageAsync(string sessionId, string role, string content, CancellationToken ct = default)
    {
        var record = await GetAsync(sessionId, ct).ConfigureAwait(false);
        if (record == null) return;
        record.Messages.Add(new SessionMessageRecord
        {
            Role = role,
            Content = content,
            TimestampUtc = DateTime.UtcNow
        });
        record.Metadata.LastUpdatedUtc = DateTime.UtcNow;
        await PersistRecordAsync(record, ct).ConfigureAwait(false);
        await UpdateIndexAsync(record.Metadata, ct).ConfigureAwait(false);
    }

    public async Task<SessionRecord?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return null;
        var path = GetSessionPath(sessionId);
        if (!File.Exists(path)) return null;
        try
        {
            await using var fs = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<SessionRecord>(fs, _jsonOptions, ct).ConfigureAwait(false);
        }
        catch { return null; }
    }

    public async Task<IReadOnlyList<SessionMetadata>> ListAsync(int? take = null, CancellationToken ct = default)
    {
        if (!File.Exists(ConfigPaths.SessionsIndexFile)) return Array.Empty<SessionMetadata>();
        try
        {
            await using var fs = File.OpenRead(ConfigPaths.SessionsIndexFile);
            var metas = await JsonSerializer.DeserializeAsync<List<SessionMetadata>>(fs, _jsonOptions, ct).ConfigureAwait(false) 
                        ?? new List<SessionMetadata>();
            var ordered = metas.OrderByDescending(m => m.LastUpdatedUtc).ToList();
            if (take.HasValue) ordered = ordered.Take(take.Value).ToList();
            return ordered;
        }
        catch { return Array.Empty<SessionMetadata>(); }
    }

    private async Task PersistRecordAsync(SessionRecord record, CancellationToken ct)
    {
        ConfigPaths.EnsureCreated();
        var path = GetSessionPath(record.Metadata.Id);
        var tmp = path + ".tmp";
        await using (var fs = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(fs, record, _jsonOptions, ct).ConfigureAwait(false);
        }
        File.Move(tmp, path, overwrite: true);
    }

    private async Task UpdateIndexAsync(SessionMetadata meta, CancellationToken ct)
    {
    // Ensure directories exist before writing index (tests may set temp base path)
    ConfigPaths.EnsureCreated();
        var list = (await ListAsync(null, ct).ConfigureAwait(false)).ToList();
        var existing = list.FindIndex(m => m.Id == meta.Id);
        if (existing >= 0) list[existing] = meta; else list.Add(meta);
        var tmp = ConfigPaths.SessionsIndexFile + ".tmp";
        await using (var fs = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(fs, list, _jsonOptions, ct).ConfigureAwait(false);
        }
        File.Move(tmp, ConfigPaths.SessionsIndexFile, overwrite: true);
    }

    private static string GenerateId() => DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
    private static string GetSessionPath(string id) => Path.Combine(ConfigPaths.SessionsDirectory, id + ".json");
}
