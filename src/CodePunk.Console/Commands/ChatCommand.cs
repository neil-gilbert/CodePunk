using Spectre.Console;

namespace CodePunk.Console.Commands;

/// <summary>
/// Represents a chat command (like /help, /new, /quit)
/// </summary>
public abstract class ChatCommand
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string[] Aliases { get; }

    public abstract Task<CommandResult> ExecuteAsync(string[] args, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the input matches this command
    /// </summary>
    public bool Matches(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || !input.StartsWith('/'))
            return false;

        var commandPart = input[1..].Split(' ')[0].ToLowerInvariant();
        return commandPart == Name.ToLowerInvariant() || 
               Aliases.Any(alias => alias.ToLowerInvariant() == commandPart);
    }

    /// <summary>
    /// Parses command arguments from input
    /// </summary>
    public string[] ParseArgs(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Array.Empty<string>();

        var parts = input[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[1..] : Array.Empty<string>();
    }
}

/// <summary>
/// Result of executing a chat command
/// </summary>
public class CommandResult
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public bool ShouldExit { get; init; }
    public bool ShouldClearSession { get; init; }

    public static CommandResult Ok(string? message = null) => new() { Success = true, Message = message };
    public static CommandResult Error(string message) => new() { Success = false, Message = message };
    public static CommandResult Exit(string? message = null) => new() { Success = true, Message = message, ShouldExit = true };
    public static CommandResult ClearSession(string? message = null) => new() { Success = true, Message = message, ShouldClearSession = true };
}
