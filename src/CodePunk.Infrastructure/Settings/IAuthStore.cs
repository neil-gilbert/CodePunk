namespace CodePunk.Infrastructure.Settings;

/// <summary>
/// Manages persistent API credentials per provider for the CLI.
/// </summary>
public interface IAuthStore
{
    Task<IDictionary<string,string>> LoadAsync(CancellationToken ct = default);
    Task SetAsync(string provider, string apiKey, CancellationToken ct = default);
    Task RemoveAsync(string provider, CancellationToken ct = default);
    Task<IEnumerable<string>> ListAsync(CancellationToken ct = default);
}
