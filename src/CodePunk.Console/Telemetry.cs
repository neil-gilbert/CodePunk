using System.Diagnostics;

namespace CodePunk.Console;

/// <summary>
/// Provides telemetry and activity tracking for the CodePunk application.
/// </summary>
internal static class Telemetry
{
    /// <summary>
    /// Provides a thread-safe source for creating and tracking activities.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new("CodePunk");

    /// <summary>
    /// Creates a new activity with the specified name.
    /// </summary>
    /// <param name="name">The name of the activity to create.</param>
    /// <returns>A new Activity instance.</returns>
    public static Activity? StartActivity(string name) => ActivitySource.StartActivity(name);

    /// <summary>
    /// Stops the current activity if one exists.
    /// </summary>
    public static void StopActivity() => Activity.Current?.Stop();

    /// <summary>
    /// Logs information to the current activity.
    /// </summary>
    /// <param name="key">The tag or log key.</param>
    /// <param name="value">The tag or log value.</param>
    public static void Log(string key, object value)
    {
        Activity.Current?.SetTag(key, value?.ToString());
    }

    /// <summary>
    /// Logs an error with the current activity.
    /// </summary>
    /// <param name="exception">The exception to log.</param>
    /// <param name="context">Optional additional context for the error.</param>
    public static void LogError(Exception exception, string? context = null)
    {
        var currentActivity = Activity.Current;
        if (currentActivity != null)
        {
            currentActivity.SetStatus(ActivityStatusCode.Error, exception.Message);
            Log("error.type", exception.GetType().FullName);
            Log("error.message", exception.Message);
            Log("error.stack_trace", exception.StackTrace);
            
            if (!string.IsNullOrWhiteSpace(context))
            {
                Log("error.context", context);
            }
        }
    }
}