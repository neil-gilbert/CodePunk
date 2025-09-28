using CodePunk.Core.Models.FileEdit;

namespace CodePunk.Core.Abstractions;

/// <summary>
/// Service for handling user approval of file edit operations
/// </summary>
public interface IApprovalService
{
    /// <summary>
    /// Request user approval for a file edit operation with diff preview
    /// </summary>
    Task<ApprovalResult> RequestApprovalAsync(FileEditRequest request, string diff, DiffStats stats, CancellationToken cancellationToken = default);
}