using CodePunk.Core.Models.FileEdit;

namespace CodePunk.Core.Abstractions;

/// <summary>
/// Service for generating unified diffs and computing diff statistics
/// </summary>
public interface IDiffService
{
    /// <summary>
    /// Create a unified diff between two text strings
    /// </summary>
    string CreateUnifiedDiff(string fileName, string oldText, string newText, int context = 3);

    /// <summary>
    /// Compute statistics for changes made by AI and user modifications
    /// </summary>
    DiffStats ComputeStats(string original, string aiProposal, string userFinal);
}