using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodePunk.Console.Stores;

public class AgentFileStore : IAgentStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task CreateAsync(AgentDefinition definition, bool overwrite = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.Name)) throw new ArgumentException("Agent name required", nameof(definition));
        ConfigPaths.EnsureCreated();
        var path = GetPath(definition.Name);
        if (File.Exists(path) && !overwrite)
            throw new InvalidOperationException($"Agent '{definition.Name}' already exists. Use overwrite flag to replace.");
        Directory.CreateDirectory(ConfigPaths.AgentsDirectory);
        var tmp = path + ".tmp";
        await using (var fs = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(fs, definition, _jsonOptions, ct).ConfigureAwait(false);
        }
        File.Move(tmp, path, overwrite: true);
    }

    public async Task<AgentDefinition?> GetAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var path = GetPath(name);
        if (!File.Exists(path)) return null;
        await using var fs = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<AgentDefinition>(fs, _jsonOptions, ct).ConfigureAwait(false);
    }

    public async Task<IEnumerable<AgentDefinition>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(ConfigPaths.AgentsDirectory)) return Array.Empty<AgentDefinition>();
        var list = new List<AgentDefinition>();
        foreach (var file in Directory.EnumerateFiles(ConfigPaths.AgentsDirectory, "*.json"))
        {
            try
            {
                await using var fs = File.OpenRead(file);
                var def = await JsonSerializer.DeserializeAsync<AgentDefinition>(fs, _jsonOptions, ct).ConfigureAwait(false);
                if (def != null) list.Add(def);
            }
            catch { /* ignore individual failures */ }
        }
        return list.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public Task DeleteAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return Task.CompletedTask;
        var path = GetPath(name);
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { }
        }
        return Task.CompletedTask;
    }

    private static string GetPath(string name) => Path.Combine(ConfigPaths.AgentsDirectory, Sanitize(name) + ".json");
    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }
}
