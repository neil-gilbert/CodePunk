namespace CodePunk.Console.Planning;

public class PlanAiGenerationOptions
{
    public int MaxFiles { get; set; } = 20;
    public int MaxPathLength { get; set; } = 260;
    public int MaxPerFileBytes { get; set; } = 16 * 1024;
    public int MaxTotalBytes { get; set; } = 128 * 1024;
    public int RetryInvalidOutput { get; set; } = 1;
    public string[] SecretPatterns { get; set; } = new [] { "API_KEY=", "SECRET=", "PASSWORD=", "-----BEGIN" };
    public int MaxModelOutputBytes { get; set; } = 256 * 1024;
}