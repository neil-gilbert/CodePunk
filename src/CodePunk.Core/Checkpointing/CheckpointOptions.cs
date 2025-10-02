namespace CodePunk.Core.Checkpointing;

public class CheckpointOptions
{
    public const string SectionName = "Checkpointing";

    public bool Enabled { get; set; } = true;

    public string? CheckpointDirectory { get; set; }

    public int MaxCheckpoints { get; set; } = 100;

    public bool AutoPrune { get; set; } = true;
}
