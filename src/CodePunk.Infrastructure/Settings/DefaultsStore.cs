using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodePunk.Infrastructure.Settings;

public record CodePunkDefaults(
    string? Provider,
    string? Model
);

public interface IDefaultsStore
{
    Task<CodePunkDefaults> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(CodePunkDefaults defaults, CancellationToken ct = default);
}

public class DefaultsFileStore : IDefaultsStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<CodePunkDefaults> LoadAsync(CancellationToken ct = default)
    {
        ConfigPaths.EnsureCreated();
        if (!File.Exists(ConfigPaths.DefaultsFile))
            return new CodePunkDefaults(null, null);
        try
        {
            await using var fs = File.OpenRead(ConfigPaths.DefaultsFile);
            var data = await JsonSerializer.DeserializeAsync<CodePunkDefaults>(fs, _jsonOptions, ct);
            return data ?? new CodePunkDefaults(null, null);
        }
        catch
        {
            return new CodePunkDefaults(null, null);
        }
    }

    public async Task SaveAsync(CodePunkDefaults defaults, CancellationToken ct = default)
    {
        ConfigPaths.EnsureCreated();
        var tmp = ConfigPaths.DefaultsFile + ".tmp";
        await using (var fs = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(fs, defaults, _jsonOptions, ct).ConfigureAwait(false);
        }
        File.Move(tmp, ConfigPaths.DefaultsFile, overwrite: true);
        TryRestrictPermissions(ConfigPaths.DefaultsFile);
    }

    private void TryRestrictPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            try { _ = System.Diagnostics.Process.Start("chmod", $"600 \"{path}\""); } catch { }
        }
    }
}
