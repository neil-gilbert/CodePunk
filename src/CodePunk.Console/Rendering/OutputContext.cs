namespace CodePunk.Console.Rendering;

public static class OutputContext
{
    public static bool IsQuiet() => !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("CODEPUNK_QUIET"));
}
