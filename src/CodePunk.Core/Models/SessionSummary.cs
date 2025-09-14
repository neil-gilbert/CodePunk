using System.Collections.Generic;

namespace CodePunk.Core.Models
{
    public class SessionSummary
    {
        public string Goal { get; set; } = string.Empty;
        public List<string> CandidateFiles { get; set; } = new List<string>();
        public string Rationale { get; set; } = string.Empty;
        public bool Truncated { get; set; }
        public int UsedMessages { get; set; }
        public int TotalMessages { get; set; }
    }
}
