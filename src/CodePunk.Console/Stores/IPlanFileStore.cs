namespace CodePunk.Console.Stores;

public interface IPlanFileStore
{
    Task<string> CreateAsync(string goal, CancellationToken ct = default);
    Task<PlanRecord?> GetAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<PlanDefinition>> ListAsync(int? take = null, CancellationToken ct = default);
    Task SaveAsync(PlanRecord record, CancellationToken ct = default);
}

public class PlanDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
        = DateTime.UtcNow;
}

public class PlanFileChange
{
    public string Path { get; set; } = string.Empty;
    public string? HashBefore { get; set; }
    public string? HashAfter { get; set; }
    public string? Diff { get; set; }
    public string? Rationale { get; set; }
    public string? BeforeContent { get; set; }
    public string? AfterContent { get; set; }
}

public class PlanRecord
{
    public PlanDefinition Definition { get; set; } = new();
    public List<PlanFileChange> Files { get; set; } = new();
}