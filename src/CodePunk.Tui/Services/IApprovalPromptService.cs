using CodePunk.Core.Abstractions;
using CodePunk.Core.Models.FileEdit;

namespace CodePunk.Tui.Services;

public interface IApprovalPromptService
{
    ApprovalPendingRequest? Current { get; }
    event Action? Changed;
    Task<ApprovalResult> RequestAsync(FileEditRequest request, string diff, DiffStats stats, CancellationToken ct = default);
    bool WasApproveAll { get; }
    void ApproveOnce();
    void ApproveAll();
    void Cancel();
}

public sealed class ApprovalPendingRequest
{
    public required FileEditRequest Request { get; init; }
    public required string Diff { get; init; }
    public required DiffStats Stats { get; init; }
}
