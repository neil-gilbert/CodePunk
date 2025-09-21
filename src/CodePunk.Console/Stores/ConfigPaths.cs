using System.Runtime.InteropServices;

namespace CodePunk.Console.Stores;

/// <summary>
/// Resolves base configuration paths for CodePunk CLI related persistence (auth, agents, sessions).
/// </summary>
internal static class ConfigPaths
{
    // Compute each access so tests can override via environment variable before use.
    public static string BaseConfigDirectory => ResolveBaseConfigDir();
    public static string AuthFile => Path.Combine(BaseConfigDirectory, "auth.json");
    public static string AgentsDirectory => Path.Combine(BaseConfigDirectory, "agents");
    public static string SessionsDirectory => Path.Combine(BaseConfigDirectory, "sessions");
    public static string SessionsIndexFile => Path.Combine(SessionsDirectory, "index.json");
    public static string PlansDirectory => Path.Combine(BaseConfigDirectory, "plans");
    public static string PlansIndexFile => Path.Combine(PlansDirectory, "index.json");
    public static string PlanBackupsDirectory => Path.Combine(PlansDirectory, "backups");
    public static string DefaultsFile => Path.Combine(BaseConfigDirectory, "defaults.json");

    private static string ResolveBaseConfigDir()
    {
        var overrideDir = Environment.GetEnvironmentVariable("CODEPUNK_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            return Path.GetFullPath(overrideDir);
        }
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "CodePunk");
        }
        // Unix-like: use XDG if set, else ~/.config
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
        {
            return Path.Combine(xdg, "codepunk");
        }
        return Path.Combine(home, ".config", "codepunk");
    }

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(BaseConfigDirectory);
        Directory.CreateDirectory(AgentsDirectory);
        Directory.CreateDirectory(SessionsDirectory);
    Directory.CreateDirectory(PlansDirectory);
    Directory.CreateDirectory(PlanBackupsDirectory);
    }
}
