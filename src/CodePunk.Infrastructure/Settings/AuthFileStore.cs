using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodePunk.Infrastructure.Settings;

/// <summary>
/// File based implementation storing provider -> apiKey in JSON.
/// </summary>
public class AuthFileStore : IAuthStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<IDictionary<string, string>> LoadAsync(CancellationToken ct = default)
    {
        ConfigPaths.EnsureCreated();
        if (!File.Exists(ConfigPaths.AuthFile))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        try
        {
            await using var fs = File.OpenRead(ConfigPaths.AuthFile);
            var data = await JsonSerializer.DeserializeAsync<Dictionary<string,string>>(fs, _jsonOptions, ct)
                       ?? new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            var sanitized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in data)
            {
                sanitized[kvp.Key] = Sanitize(kvp.Value);
            }
            return sanitized;
        }
        catch
        {
            try
            {
                var backup = ConfigPaths.AuthFile + ".bak-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                File.Copy(ConfigPaths.AuthFile, backup, overwrite: true);
            }
            catch { /* ignore */ }
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public async Task SetAsync(string provider, string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(provider)) throw new ArgumentException("Provider required", nameof(provider));
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("API key required", nameof(apiKey));
        var map = await LoadAsync(ct).ConfigureAwait(false);
        map[provider] = Sanitize(apiKey);
        await PersistAsync(map, ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string provider, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(provider)) return;
        var map = await LoadAsync(ct).ConfigureAwait(false);
        if (map.Remove(provider))
        {
            await PersistAsync(map, ct).ConfigureAwait(false);
        }
    }

    public async Task<IEnumerable<string>> ListAsync(CancellationToken ct = default)
    {
        var map = await LoadAsync(ct).ConfigureAwait(false);
        return map.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task PersistAsync(IDictionary<string,string> map, CancellationToken ct)
    {
        ConfigPaths.EnsureCreated();
        var tmp = ConfigPaths.AuthFile + ".tmp";
        await using (var fs = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(fs, map, _jsonOptions, ct).ConfigureAwait(false);
        }
        if (File.Exists(ConfigPaths.AuthFile))
        {
            var bak = ConfigPaths.AuthFile + ".prev";
            try { File.Copy(ConfigPaths.AuthFile, bak, overwrite: true); } catch { }
        }
        File.Move(tmp, ConfigPaths.AuthFile, overwrite: true);
        TryRestrictPermissions(ConfigPaths.AuthFile);
    }

    private void TryRestrictPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            try { _ = System.Diagnostics.Process.Start("chmod", $"600 \"{path}\""); } catch { }
        }
    }

    private static string Sanitize(string value)
    {
        return (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
    }
}
