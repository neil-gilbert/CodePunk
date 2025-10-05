using System.Diagnostics;

namespace CodePunk.Console;

/// <summary>
/// Provides telemetry and activity tracking for the CodePunk application.
/// </summary>
internal static class Telemetry
{
    /// <summary>
    /// The name of the telemetry source.
    /// </summary>
    private const string SourceName = "CodePunk";

    /// <summary>
    /// Provides a thread-safe source for creating and tracking activities.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(SourceName);

    /// <summary>
    /// Creates a new activity with the specified name.
    /// </summary>
    /// <param name="name">The name of the activity to create.</param>
    /// <returns>A new Activity instance.</returns>
    public static Activity? StartActivity(string name)
    {
        return ActivitySource.StartActivity(name);
    }

    /// <summary>
    /// Stops the current activity if one exists.
    /// </summary>
    public static void StopActivity()
    {
        Activity.Current?.Stop();
    }

    /// <summary>
    /// Adds a tag to the current activity if one exists.
    /// </summary>
    /// <param name="key">The tag key.</param>
    /// <param name="value">The tag value.</param>
    public static void AddTag(string key, string value)
    {
        Activity.Current?.SetTag(key, value);
    }

    /// <summary>
    /// Logs an error with the current activity.
    /// </summary>
    /// <param name="exception">The exception to log.</param>
    /// <param name="additionalContext">Optional additional context for the error.</param>
    public static void LogError(Exception exception, string? additionalContext = null)
    {
        var currentActivity = Activity.Current;
        if (currentActivity != null)
        {
            currentActivity.SetStatus(ActivityStatusCode.Error, exception.Message);
            currentActivity.SetTag("error.type", exception.GetType().FullName);
            currentActivity.SetTag("error.stack_trace", exception.StackTrace);
            
            if (!string.IsNullOrWhiteSpace(additionalContext))
            {
                currentActivity.SetTag("error.context", additionalContext);
            }
        }
    }

    /// <summary>
    /// Logs an informational event.
    /// </summary>
    /// <param name="message">The informational message.</param>
    public static void LogInfo(string message)
    {
        Activity.Current?.SetTag("info", message);
    }
}