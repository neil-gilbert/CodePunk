using System.Threading;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models.FileEdit;

namespace CodePunk.Tui.Services;

public sealed class ApprovalPromptService : IApprovalPromptService
{
    private TaskCompletionSource<ApprovalResult>? _tcs;
    private readonly object _gate = new();

    public ApprovalPendingRequest? Current { get; private set; }
    public event Action? Changed;
    public bool WasApproveAll { get; private set; }

    public Task<ApprovalResult> RequestAsync(FileEditRequest request, string diff, DiffStats stats, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_tcs != null) throw new InvalidOperationException("Another approval is already pending.");
            _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Current = new ApprovalPendingRequest { Request = request, Diff = diff ?? string.Empty, Stats = stats };
        }
        Changed?.Invoke();

        if (ct.CanBeCanceled)
        {
            ct.Register(() => TryResolve(new ApprovalResult(false)));
        }
        return _tcs.Task;
    }

    public void ApproveOnce()
    {
        WasApproveAll = false;
        TryResolve(new ApprovalResult(true));
    }

    public void ApproveAll()
    {
        WasApproveAll = true;
        TryResolve(new ApprovalResult(true));
    }

    public void Cancel()
    {
        WasApproveAll = false;
        TryResolve(new ApprovalResult(false));
    }

    private void TryResolve(ApprovalResult result)
    {
        TaskCompletionSource<ApprovalResult>? tcs;
        lock (_gate)
        {
            tcs = _tcs;
            _tcs = null;
            Current = null;
        }
        if (tcs != null)
        {
            tcs.TrySetResult(result);
            Changed?.Invoke();
        }
    }
}
