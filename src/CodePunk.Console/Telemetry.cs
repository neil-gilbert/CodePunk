using System.Diagnostics;

namespace CodePunk.Console;

internal static class Telemetry
{
    public static readonly ActivitySource ActivitySource = new("CodePunk");
}