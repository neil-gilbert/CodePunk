/// <summary>
/// Provides telemetry and activity tracking capabilities for the CodePunk application.
/// Uses System.Diagnostics.ActivitySource for distributed tracing and monitoring.
/// </summary>using System.Diagnostics;
using System.Diagnostics;
namespace CodePunk.Console;

internal static class Telemetry
{
    public static readonly ActivitySource ActivitySource = new("CodePunk");
}