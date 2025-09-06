namespace CodePunk.Console.Stores;

public class AgentDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string? PromptFilePath { get; set; }
    public string[]? Tools { get; set; }
}
