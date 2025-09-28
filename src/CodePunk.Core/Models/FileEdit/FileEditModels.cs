namespace CodePunk.Core.Models.FileEdit;

/// <summary>
/// Base class for file edit requests
/// </summary>
public abstract record FileEditRequest(string FilePath, bool RequireApproval = true);

/// <summary>
/// Request to write complete content to a file
/// </summary>
public record WriteFileRequest(
    string FilePath,
    string Content,
    bool RequireApproval = true) : FileEditRequest(FilePath, RequireApproval);

/// <summary>
/// Request to replace exact text in a file with new content
/// </summary>
public record ReplaceRequest(
    string FilePath,
    string OldString,
    string NewString,
    int? ExpectedOccurrences = null,
    bool RequireApproval = true) : FileEditRequest(FilePath, RequireApproval);

/// <summary>
/// Result of a file edit operation
/// </summary>
public record FileEditResult(
    bool Success,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    string? UnifiedDiff = null,
    DiffStats? Stats = null,
    int? TokensSaved = null);

/// <summary>
/// Statistics about changes made in a diff
/// </summary>
public record DiffStats(
    int LinesAdded,
    int LinesRemoved,
    int CharsAdded,
    int CharsRemoved);

/// <summary>
/// Result of user approval request
/// </summary>
public record ApprovalResult(
    bool Approved,
    string? ModifiedContent = null);

/// <summary>
/// Result of file validation
/// </summary>
public record ValidationResult(
    bool IsValid,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static ValidationResult Success() => new(true);
    public static ValidationResult Error(string errorCode, string? errorMessage = null) =>
        new(false, errorCode, errorMessage);
}

/// <summary>
/// Strategy for handling large files
/// </summary>
public enum EditStrategy
{
    FullDiff,
    SummaryOnly,
    RegionDiff,
    RequireForce
}

/// <summary>
/// Standard error codes for file editing operations
/// </summary>
public static class FileEditErrorCodes
{
    public const string FileNotFound = "FILE_NOT_FOUND";
    public const string OccurrenceMismatch = "OCCURRENCE_MISMATCH";
    public const string NoOccurrence = "NO_OCCURRENCE";
    public const string NoChange = "NO_CHANGE";
    public const string PermissionDenied = "PERMISSION_DENIED";
    public const string WriteFailed = "WRITE_FAILED";
    public const string PathOutOfRoot = "PATH_OUT_OF_ROOT";
    public const string BinaryFile = "BINARY_FILE";
    public const string FileTooLarge = "FILE_TOO_LARGE";
    public const string Conflict = "CONFLICT";
}