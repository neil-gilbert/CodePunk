using CodePunk.Core.Abstractions;
using CodePunk.Core.Models.FileEdit;
using Microsoft.Extensions.Logging;
using CodePunk.Tui.Services;

namespace CodePunk.Tui.Adapters;

public class RazorApprovalService : IApprovalService
{
    private readonly ILogger<RazorApprovalService> _logger;
    private readonly IApprovalPromptService _prompt;
    private bool _autoApproveSession;

    public RazorApprovalService(ILogger<RazorApprovalService> logger, IApprovalPromptService prompt)
    {
        _logger = logger;
        _prompt = prompt;
    }

    public Task<ApprovalResult> RequestApprovalAsync(
        FileEditRequest request,
        string diff,
        DiffStats stats,
        CancellationToken cancellationToken = default)
    {
        if (_autoApproveSession)
        {
            _logger.LogInformation("Auto-approving (session) change: {FilePath}", request.FilePath);
            return Task.FromResult(new ApprovalResult(true));
        }
        _logger.LogInformation("Requesting approval: {FilePath} (+{Added}/-{Removed})", request.FilePath, stats.LinesAdded, stats.LinesRemoved);
        return AwaitAndMaybeEnableAutoAsync(request, diff, stats, cancellationToken);
    }

    private async Task<ApprovalResult> AwaitAndMaybeEnableAutoAsync(FileEditRequest request, string diff, DiffStats stats, CancellationToken ct)
    {
        var result = await _prompt.RequestAsync(request, diff, stats, ct).ConfigureAwait(false);
        if (result.Approved && _prompt.WasApproveAll)
        {
            _autoApproveSession = true;
        }
        return result;
    }
}
