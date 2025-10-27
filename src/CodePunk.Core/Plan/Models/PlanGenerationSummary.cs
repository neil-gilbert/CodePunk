namespace CodePunk.Core.Models
{
    public class PlanGenerationSummary
    {
        public required string PlanId { get; set; }
        public required string Goal { get; set; }
        public required string Provider { get; set; }
        public required string Model { get; set; }
        public required List<PlanFileSummary> Files { get; set; } = new();
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
    }
}

