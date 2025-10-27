using System.CommandLine;
using System.Diagnostics;
using System.IO;

namespace CodePunk.Console.Commands.Modules;

internal sealed class TuiCommandModule : ICommandModule
{
    public void Register(RootCommand root, IServiceProvider services)
    {
        root.Add(BuildTui());
    }

    private static Command BuildTui()
    {
        var cmd = new Command("tui", "Launch the RazorConsole-based UI (developer mode)");
        cmd.SetHandler(() =>
        {
            try
            {
                // Allow override
                var overrideCmd = Environment.GetEnvironmentVariable("CODEPUNK_TUI_RUN");
                if (!string.IsNullOrWhiteSpace(overrideCmd))
                {
                    RunShell(overrideCmd!);
                    return;
                }

                // Default: dotnet run the TUI project if present
                var cwd = Directory.GetCurrentDirectory();
                var projPath = Path.Combine(cwd, "src", "CodePunk.Tui", "CodePunk.Tui.csproj");
                if (!File.Exists(projPath))
                {
                    System.Console.WriteLine("CodePunk.Tui project not found at src/CodePunk.Tui.\n" +
                                              "Build or run it manually: dotnet run --project src/CodePunk.Tui");
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    ArgumentList = { "run", "--project", projPath },
                    UseShellExecute = false,
                    RedirectStandardInput = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit();
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"Failed to launch TUI: {ex.Message}");
            }
        });
        return cmd;
    }

    private static void RunShell(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = GetShell(),
            Arguments = GetShellArgs(command),
            UseShellExecute = false,
        };
        using var p = Process.Start(psi);
        p?.WaitForExit();
    }

    private static string GetShell()
        => OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash";

    private static string GetShellArgs(string cmd)
        => OperatingSystem.IsWindows() ? $"/c {cmd}" : $"-lc \"{cmd}\"";
}

