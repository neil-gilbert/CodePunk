using System.Collections.Concurrent;
using CodePunk.Core.Abstractions;

namespace CodePunk.Core.Services;

public static class RuntimeProviderRegistry
{
    private static readonly ConcurrentDictionary<string, ILLMProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    public static void RegisterOrUpdate(ILLMProvider provider)
    {
        if (provider == null || string.IsNullOrWhiteSpace(provider.Name)) return;
        _providers[provider.Name] = provider;
    }

    public static bool TryGet(string name, out ILLMProvider provider) => _providers.TryGetValue(name, out provider!);

    public static IEnumerable<string> GetNames() => _providers.Keys.ToArray();
}