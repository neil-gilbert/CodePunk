using Microsoft.Extensions.Logging;

namespace CodePunk.Console.Commands;

/// <summary>
/// Processes and executes chat commands
/// </summary>
public class CommandProcessor
{
    private readonly List<ChatCommand> _commands;
    private readonly ILogger<CommandProcessor> _logger;

    public CommandProcessor(IEnumerable<ChatCommand> commands, ILogger<CommandProcessor> logger)
    {
        _commands = commands.ToList();
        _logger = logger;
    }

    /// <summary>
    /// Checks if the input is a command (starts with /)
    /// </summary>
    public bool IsCommand(string input)
    {
        return !string.IsNullOrWhiteSpace(input) && input.StartsWith('/');
    }

    /// <summary>
    /// Finds the command that matches the input
    /// </summary>
    public ChatCommand? FindCommand(string input)
    {
        return _commands.FirstOrDefault(cmd => cmd.Matches(input));
    }

    /// <summary>
    /// Executes a command from user input
    /// </summary>
    public async Task<CommandResult> ExecuteCommandAsync(string input, CancellationToken cancellationToken = default)
    {
        if (!IsCommand(input))
        {
            return CommandResult.Error("Invalid command format. Commands must start with '/'");
        }

        var command = FindCommand(input);
        if (command == null)
        {
            var commandName = input.Split(' ')[0];
            return CommandResult.Error($"Unknown command: [red]{commandName}[/]. Type [cyan]/help[/] for available commands.");
        }

        try
        {
            _logger.LogInformation("Executing command: {CommandName}", command.Name);
            var result = await command.ExecuteAsync(command.ParseArgs(input), cancellationToken);
            _logger.LogInformation("Command executed successfully: {CommandName}", command.Name);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command: {CommandName}", command.Name);
            return CommandResult.Error($"Error executing command: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets all available commands
    /// </summary>
    public IReadOnlyList<ChatCommand> GetAllCommands() => _commands.AsReadOnly();
}
