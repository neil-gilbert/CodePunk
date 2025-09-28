using CodePunk.Core.Models.FileEdit;

namespace CodePunk.Core.Abstractions;

/// <summary>
/// Service for performing file editing operations following the Gemini CLI pattern
/// </summary>
public interface IFileEditService
{
    /// <summary>
    /// Write complete content to a file, generating diffs for approval
    /// </summary>
    Task<FileEditResult> WriteFileAsync(WriteFileRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replace exact text in a file with new content using literal matching
    /// </summary>
    Task<FileEditResult> ReplaceInFileAsync(ReplaceRequest request, CancellationToken cancellationToken = default);
}