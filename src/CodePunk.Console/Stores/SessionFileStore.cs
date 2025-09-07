using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodePunk.Console.Stores;

public class SessionFileStore : ISessionFileStore
{
    private readonly string _baseDir;
    private string SessionsDir => Path.Combine(_baseDir, "sessions");
    private string IndexFile => Path.Combine(SessionsDir, "index.json");
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public SessionFileStore()
    {
        _baseDir = ConfigPaths.BaseConfigDirectory;
    }

    public async Task<string> CreateAsync(string? title, string? agent, string? model, CancellationToken ct = default)
    {
        EnsureCreated();
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
    record.Metadata.MessageCount = record.Messages.Count;
        await PersistRecordAsync(record, ct).ConfigureAwait(false);
        await UpdateIndexAsync(record.Metadata, ct).ConfigureAwait(false);
    }

    public async Task<SessionRecord?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return null;
        var path = GetSessionPath(sessionId);
        if (!File.Exists(path)) return null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await using var fs = File.OpenRead(path);
                var rec = await JsonSerializer.DeserializeAsync<SessionRecord>(fs, _jsonOptions, ct).ConfigureAwait(false);
                if (rec != null) return rec;
            }
            catch when (attempt < 2)
            {
                await Task.Delay(15, ct).ConfigureAwait(false);
            }
        }
        return null;
    }

    public async Task<IReadOnlyList<SessionMetadata>> ListAsync(int? take = null, CancellationToken ct = default)
    {
        if (!File.Exists(IndexFile))
        {
            if (!Directory.Exists(SessionsDir)) return Array.Empty<SessionMetadata>();
            var metas = new List<SessionMetadata>();
            foreach (var f in Directory.EnumerateFiles(SessionsDir, "*.json"))
            {
                if (Path.GetFileName(f).Equals("index.json", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    await using var fsr = File.OpenRead(f);
                    var rec = await JsonSerializer.DeserializeAsync<SessionRecord>(fsr, _jsonOptions, ct).ConfigureAwait(false);
                    if (rec?.Metadata != null)
                    {
                        rec.Metadata.MessageCount = rec.Messages?.Count ?? 0;
                        metas.Add(rec.Metadata);
                    }
                }
                catch { }
            }
            var orderedFallback = metas.OrderByDescending(m => m.LastUpdatedUtc).ToList();
            if (orderedFallback.Count > 0)
            {
                try
                {
                    var tmpIdx = IndexFile + ".tmp";
                    await using (var fsw = File.Create(tmpIdx))
                    {
                        await JsonSerializer.SerializeAsync(fsw, orderedFallback, _jsonOptions, ct).ConfigureAwait(false);
                    }
                    File.Move(tmpIdx, IndexFile, overwrite: true);
                }
                catch { }
            }
            if (take.HasValue) orderedFallback = orderedFallback.Take(take.Value).ToList();
            return orderedFallback;
        }
        try
        {
            await using var fs = File.OpenRead(IndexFile);
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
        EnsureCreated();
        var path = GetSessionPath(record.Metadata.Id);
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        await using (var fs = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(fs, record, _jsonOptions, ct).ConfigureAwait(false);
            await fs.FlushAsync(ct).ConfigureAwait(false);
        }
        File.Move(tmp, path, overwrite: true);
    }

    private async Task UpdateIndexAsync(SessionMetadata meta, CancellationToken ct)
    {
        EnsureCreated();
        var current = await ListAsync(null, ct).ConfigureAwait(false);
        var list = current.ToList();
        var existing = list.FindIndex(m => m.Id == meta.Id);
        if (existing >= 0)
        {
            if (meta.MessageCount == 0 && list[existing].MessageCount > 0)
                meta.MessageCount = list[existing].MessageCount;
            list[existing] = meta;
        }
        else list.Add(meta);
        var tmp = IndexFile + ".tmp";
        await using (var fs = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(fs, list, _jsonOptions, ct).ConfigureAwait(false);
        }
        File.Move(tmp, IndexFile, overwrite: true);
    }

    private static string GenerateId() => DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
    private string GetSessionPath(string id) => Path.Combine(SessionsDir, id + ".json");
    private void EnsureCreated()
    {
        Directory.CreateDirectory(_baseDir);
        Directory.CreateDirectory(SessionsDir);
    }
}
