using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodePunk.Console.Stores;

public class PlanFileStore : IPlanFileStore
{
    private readonly string _baseDir;
    private string PlansDir => Path.Combine(_baseDir, "plans");
    private string IndexFile => Path.Combine(PlansDir, "index.json");
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public PlanFileStore()
    {
        _baseDir = ConfigPaths.BaseConfigDirectory;
    }

    public async Task<string> CreateAsync(string goal, CancellationToken ct = default)
    {
        Ensure();
        var id = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
        var def = new PlanDefinition { Id = id, Goal = goal.Trim(), CreatedUtc = DateTime.UtcNow };
        var rec = new PlanRecord { Definition = def };
        await PersistAsync(rec, ct).ConfigureAwait(false);
        await UpdateIndexAsync(def, ct).ConfigureAwait(false);
        return id;
    }

    public async Task<PlanRecord?> GetAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var path = GetPlanPath(id);
        if (!File.Exists(path)) return null;
        await using var fs = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<PlanRecord>(fs, _json, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PlanDefinition>> ListAsync(int? take = null, CancellationToken ct = default)
    {
        if (!File.Exists(IndexFile)) return Array.Empty<PlanDefinition>();
        try
        {
            await using var fs = File.OpenRead(IndexFile);
            var list = await JsonSerializer.DeserializeAsync<List<PlanDefinition>>(fs, _json, ct).ConfigureAwait(false) ?? new();
            var ordered = list.OrderByDescending(p => p.CreatedUtc).ToList();
            if (take.HasValue) ordered = ordered.Take(take.Value).ToList();
            return ordered;
        }
        catch { return Array.Empty<PlanDefinition>(); }
    }

    public Task SaveAsync(PlanRecord record, CancellationToken ct = default)
    {
        return PersistAsync(record, ct);
    }

    private async Task PersistAsync(PlanRecord rec, CancellationToken ct)
    {
        Ensure();
        var path = GetPlanPath(rec.Definition.Id);
        var tmp = path + ".tmp";
        await using (var fs = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(fs, rec, _json, ct).ConfigureAwait(false);
        }
        File.Move(tmp, path, true);
    }

    private async Task UpdateIndexAsync(PlanDefinition def, CancellationToken ct)
    {
        Ensure();
        List<PlanDefinition> list = new();
        if (File.Exists(IndexFile))
        {
            try
            {
                await using var fs = File.OpenRead(IndexFile);
                list = await JsonSerializer.DeserializeAsync<List<PlanDefinition>>(fs, _json, ct).ConfigureAwait(false) ?? new();
            }
            catch { }
        }
        var existing = list.FindIndex(p => p.Id == def.Id);
        if (existing >= 0) list[existing] = def; else list.Add(def);
        var tmp = IndexFile + ".tmp";
        await using (var fsw = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(fsw, list, _json, ct).ConfigureAwait(false);
        }
        File.Move(tmp, IndexFile, true);
    }

    private string GetPlanPath(string id) => Path.Combine(PlansDir, id + ".json");
    private void Ensure()
    {
        Directory.CreateDirectory(_baseDir);
        Directory.CreateDirectory(PlansDir);
    }

    internal static string Sha256(string content)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }
}