using System.Threading;
using System.Threading.Tasks;
using CodePunk.Core.Models;

namespace CodePunk.Core.Abstractions
{
    public interface ISessionSummarizer
    {
        Task<SessionSummary?> SummarizeAsync(string sessionId, SessionSummaryOptions options, CancellationToken ct = default);
    }
}
