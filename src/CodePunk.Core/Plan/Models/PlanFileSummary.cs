namespace CodePunk.Core.Models
{
    public class PlanFileSummary
    {
        public required string Path { get; set; }
        public bool IsDelete { get; set; }
        public string? Rationale { get; set; }
        public bool Generated { get; set; }
        public List<string>? Diagnostics { get; set; }
    }
}

