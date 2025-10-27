namespace CodePunk.Core.Models
{
    public class SessionSummaryOptions
    {
        public int MaxMessages { get; set; } = 20;
        public bool IncludeToolMessages { get; set; } = false;
    }
}

