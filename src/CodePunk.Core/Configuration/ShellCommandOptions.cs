namespace CodePunk.Core.Configuration;

public class ShellCommandOptions
{
    public const string SectionName = "Shell";

    public List<string> AllowedCommands { get; set; } = new();
    public List<string> BlockedCommands { get; set; } = new();
    public bool EnableCommandValidation { get; set; } = true;
}
