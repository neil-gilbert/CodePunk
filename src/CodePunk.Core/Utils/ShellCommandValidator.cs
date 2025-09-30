using System.Text.RegularExpressions;

namespace CodePunk.Core.Utils;

public static class ShellCommandValidator
{
    public static bool ContainsCommandSubstitution(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var inSingleQuotes = false;
        var inDoubleQuotes = false;
        var inBackticks = false;

        for (var i = 0; i < command.Length; i++)
        {
            var currentChar = command[i];
            var nextChar = i < command.Length - 1 ? command[i + 1] : '\0';

            if (currentChar == '\\' && !inSingleQuotes)
            {
                i++;
                continue;
            }

            if (currentChar == '\'' && !inDoubleQuotes && !inBackticks)
            {
                inSingleQuotes = !inSingleQuotes;
            }
            else if (currentChar == '"' && !inSingleQuotes && !inBackticks)
            {
                inDoubleQuotes = !inDoubleQuotes;
            }
            else if (currentChar == '`' && !inSingleQuotes)
            {
                inBackticks = !inBackticks;
            }

            if (!inSingleQuotes)
            {
                if (currentChar == '$' && nextChar == '(')
                {
                    return true;
                }

                if (currentChar == '<' && nextChar == '(' && !inDoubleQuotes && !inBackticks)
                {
                    return true;
                }

                if (currentChar == '>' && nextChar == '(' && !inDoubleQuotes && !inBackticks)
                {
                    return true;
                }

                if (currentChar == '`' && !inBackticks)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static List<string> SplitCommandChain(string command)
    {
        var commands = new List<string>();
        var currentCommand = string.Empty;
        var inSingleQuotes = false;
        var inDoubleQuotes = false;
        var i = 0;

        while (i < command.Length)
        {
            var currentChar = command[i];
            var nextChar = i < command.Length - 1 ? command[i + 1] : '\0';

            if (currentChar == '\\' && i < command.Length - 1)
            {
                currentCommand += command.Substring(i, 2);
                i += 2;
                continue;
            }

            if (currentChar == '\'' && !inDoubleQuotes)
            {
                inSingleQuotes = !inSingleQuotes;
            }
            else if (currentChar == '"' && !inSingleQuotes)
            {
                inDoubleQuotes = !inDoubleQuotes;
            }

            if (!inSingleQuotes && !inDoubleQuotes)
            {
                if ((currentChar == '&' && nextChar == '&') || (currentChar == '|' && nextChar == '|'))
                {
                    commands.Add(currentCommand.Trim());
                    currentCommand = string.Empty;
                    i++;
                }
                else if (currentChar == ';' || currentChar == '&' || currentChar == '|')
                {
                    commands.Add(currentCommand.Trim());
                    currentCommand = string.Empty;
                }
                else
                {
                    currentCommand += currentChar;
                }
            }
            else
            {
                currentCommand += currentChar;
            }

            i++;
        }

        if (!string.IsNullOrWhiteSpace(currentCommand))
        {
            commands.Add(currentCommand.Trim());
        }

        return commands.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
    }

    public static string? GetCommandRoot(string command)
    {
        var trimmedCommand = command.Trim();
        if (string.IsNullOrWhiteSpace(trimmedCommand))
        {
            return null;
        }

        var match = Regex.Match(trimmedCommand, @"^""([^""]+)""|^'([^']+)'|^(\S+)");
        if (match.Success)
        {
            var commandRoot = match.Groups[1].Value;
            if (string.IsNullOrEmpty(commandRoot))
            {
                commandRoot = match.Groups[2].Value;
            }
            if (string.IsNullOrEmpty(commandRoot))
            {
                commandRoot = match.Groups[3].Value;
            }

            if (!string.IsNullOrEmpty(commandRoot))
            {
                var parts = commandRoot.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                return parts.Length > 0 ? parts[^1] : null;
            }
        }

        return null;
    }

    public static List<string> GetCommandRoots(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new List<string>();
        }

        return SplitCommandChain(command)
            .Select(GetCommandRoot)
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Cast<string>()
            .ToList();
    }

    public static CommandValidationResult ValidateCommand(
        string command,
        List<string>? allowedCommands = null,
        List<string>? blockedCommands = null)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new CommandValidationResult
            {
                IsValid = false,
                ErrorMessage = "Command cannot be empty"
            };
        }

        if (ContainsCommandSubstitution(command))
        {
            return new CommandValidationResult
            {
                IsValid = false,
                ErrorMessage = "Command substitution using $(), ``, <(), or >() is not allowed for security reasons"
            };
        }

        var commandRoots = GetCommandRoots(command);
        if (commandRoots.Count == 0)
        {
            return new CommandValidationResult
            {
                IsValid = false,
                ErrorMessage = "Could not identify command root to validate"
            };
        }

        var chainedCommands = SplitCommandChain(command);

        if (blockedCommands != null && blockedCommands.Count > 0)
        {
            foreach (var cmd in chainedCommands)
            {
                var trimmedCmd = cmd.Trim();
                var cmdRoot = GetCommandRoot(trimmedCmd);

                if (cmdRoot != null)
                {
                    foreach (var blocked in blockedCommands)
                    {
                        if (trimmedCmd.StartsWith(blocked, StringComparison.OrdinalIgnoreCase) ||
                            cmdRoot.StartsWith(blocked, StringComparison.OrdinalIgnoreCase))
                        {
                            return new CommandValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = $"Command '{cmdRoot}' is blocked by configuration",
                                BlockedCommand = cmdRoot
                            };
                        }
                    }
                }
            }
        }

        if (allowedCommands != null && allowedCommands.Count > 0)
        {
            foreach (var cmd in chainedCommands)
            {
                var trimmedCmd = cmd.Trim();
                var cmdRoot = GetCommandRoot(trimmedCmd);

                if (cmdRoot != null)
                {
                    var isAllowed = false;
                    foreach (var allowed in allowedCommands)
                    {
                        if (cmdRoot.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
                        {
                            isAllowed = true;
                            break;
                        }
                    }

                    if (!isAllowed)
                    {
                        return new CommandValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = $"Command '{cmdRoot}' is not in the allowed commands list",
                            BlockedCommand = cmdRoot
                        };
                    }
                }
            }
        }

        return new CommandValidationResult
        {
            IsValid = true,
            CommandRoots = commandRoots
        };
    }
}

public class CommandValidationResult
{
    public bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
    public string? BlockedCommand { get; init; }
    public List<string> CommandRoots { get; init; } = new();
}
