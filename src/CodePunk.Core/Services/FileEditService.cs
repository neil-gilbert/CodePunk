using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models.FileEdit;

namespace CodePunk.Core.Services;

/// <summary>
/// Service for performing file editing operations with validation and atomic writes
/// </summary>
public class FileEditService : IFileEditService
{
    private readonly IDiffService _diffService;
    private readonly IApprovalService _approvalService;
    private readonly ILogger<FileEditService> _logger;

    public FileEditService(
        IDiffService diffService,
        IApprovalService approvalService,
        ILogger<FileEditService> logger)
    {
        _diffService = diffService;
        _approvalService = approvalService;
        _logger = logger;
    }

    /// <summary>
    /// Write complete content to a file with diff generation and approval
    /// </summary>
    public async Task<FileEditResult> WriteFileAsync(WriteFileRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate file path
            var validation = ValidateFilePath(request.FilePath);
            if (!validation.IsValid)
            {
                return new FileEditResult(false, validation.ErrorCode, validation.ErrorMessage);
            }

            var fullPath = Path.GetFullPath(request.FilePath, Directory.GetCurrentDirectory());
            var fileExists = File.Exists(fullPath);

            // Validate file for editing
            if (fileExists)
            {
                var fileValidation = await ValidateFileForEdit(fullPath, cancellationToken);
                if (!fileValidation.IsValid)
                {
                    return new FileEditResult(false, fileValidation.ErrorCode, fileValidation.ErrorMessage);
                }
            }

            // Read existing content
            var originalContent = fileExists ? await File.ReadAllTextAsync(fullPath, cancellationToken) : string.Empty;
            var normalizedOriginal = NormalizeLineEndings(originalContent);
            var normalizedNew = NormalizeLineEndings(request.Content);

            // Generate diff and stats
            var diff = _diffService.CreateUnifiedDiff(request.FilePath, normalizedOriginal, normalizedNew);
            var stats = _diffService.ComputeStats(normalizedOriginal, normalizedNew, normalizedNew);

            // Handle approval if required
            if (request.RequireApproval && !string.IsNullOrEmpty(diff))
            {
                var approvalResult = await _approvalService.RequestApprovalAsync(
                    request, diff, stats, cancellationToken);

                if (!approvalResult.Approved)
                {
                    return new FileEditResult(false, "USER_CANCELLED", "User cancelled the operation");
                }

                // Use modified content if provided
                if (!string.IsNullOrEmpty(approvalResult.ModifiedContent))
                {
                    normalizedNew = NormalizeLineEndings(approvalResult.ModifiedContent);
                    diff = _diffService.CreateUnifiedDiff(request.FilePath, normalizedOriginal, normalizedNew);
                    stats = _diffService.ComputeStats(normalizedOriginal, normalizedNew, normalizedNew);
                }
            }

            // Perform atomic write
            await AtomicWriteAsync(fullPath, normalizedNew, cancellationToken);

            // Calculate token savings estimate
            var tokensSaved = CalculateTokensSaved(normalizedOriginal.Length, normalizedNew.Length, diff.Length);

            _logger.LogInformation("File written successfully: {FilePath}, Lines: +{Added}/-{Removed}",
                request.FilePath, stats.LinesAdded, stats.LinesRemoved);

            return new FileEditResult(true, null, null, diff, stats, tokensSaved);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing file: {FilePath}", request.FilePath);
            return new FileEditResult(false, FileEditErrorCodes.WriteFailed, ex.Message);
        }
    }

    /// <summary>
    /// Replace exact text in a file using literal matching
    /// </summary>
    public async Task<FileEditResult> ReplaceInFileAsync(ReplaceRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate file path
            var validation = ValidateFilePath(request.FilePath);
            if (!validation.IsValid)
            {
                return new FileEditResult(false, validation.ErrorCode, validation.ErrorMessage);
            }

            var fullPath = Path.GetFullPath(request.FilePath, Directory.GetCurrentDirectory());

            if (!File.Exists(fullPath))
            {
                return new FileEditResult(false, FileEditErrorCodes.FileNotFound, $"File not found: {request.FilePath}");
            }

            // Validate file for editing
            var fileValidation = await ValidateFileForEdit(fullPath, cancellationToken);
            if (!fileValidation.IsValid)
            {
                return new FileEditResult(false, fileValidation.ErrorCode, fileValidation.ErrorMessage);
            }

            // Read and normalize content
            var originalContent = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var normalizedContent = NormalizeLineEndings(originalContent);

            // Count occurrences
            var occurrences = CountOccurrences(normalizedContent, request.OldString);

            if (occurrences == 0)
            {
                return new FileEditResult(false, FileEditErrorCodes.NoOccurrence,
                    $"Text not found in file: '{request.OldString}'");
            }

            if (request.ExpectedOccurrences.HasValue && occurrences != request.ExpectedOccurrences.Value)
            {
                return new FileEditResult(false, FileEditErrorCodes.OccurrenceMismatch,
                    $"Expected {request.ExpectedOccurrences} occurrences, found {occurrences}");
            }

            // Apply replacement
            var newContent = normalizedContent.Replace(request.OldString, request.NewString);

            if (string.Equals(normalizedContent, newContent, StringComparison.Ordinal))
            {
                return new FileEditResult(false, FileEditErrorCodes.NoChange, "No changes made to file");
            }

            // Generate diff and stats
            var diff = _diffService.CreateUnifiedDiff(request.FilePath, normalizedContent, newContent);
            var stats = _diffService.ComputeStats(normalizedContent, newContent, newContent);

            // Handle approval if required
            if (request.RequireApproval)
            {
                var approvalResult = await _approvalService.RequestApprovalAsync(
                    request, diff, stats, cancellationToken);

                if (!approvalResult.Approved)
                {
                    return new FileEditResult(false, "USER_CANCELLED", "User cancelled the operation");
                }
            }

            // Perform atomic write
            await AtomicWriteAsync(fullPath, newContent, cancellationToken);

            var tokensSaved = CalculateTokensSaved(normalizedContent.Length, newContent.Length,
                request.OldString.Length + request.NewString.Length);

            _logger.LogInformation("File replacement completed: {FilePath}, {Occurrences} occurrences replaced",
                request.FilePath, occurrences);

            return new FileEditResult(true, null, null, diff, stats, tokensSaved);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error replacing text in file: {FilePath}", request.FilePath);
            return new FileEditResult(false, FileEditErrorCodes.WriteFailed, ex.Message);
        }
    }

    private static ValidationResult ValidateFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return ValidationResult.Error(FileEditErrorCodes.InvalidPath, "File path cannot be empty");

        try
        {
            var workspaceRoot = Path.GetFullPath(Directory.GetCurrentDirectory());
            var fullPath = Path.IsPathFullyQualified(filePath)
                ? Path.GetFullPath(filePath)
                : Path.GetFullPath(filePath, workspaceRoot);

            if (!IsPathWithinRoot(fullPath, workspaceRoot))
                return ValidationResult.Error(FileEditErrorCodes.PathOutOfRoot, "File path outside workspace");

            return ValidationResult.Success();
        }
        catch (Exception ex)
        {
            return ValidationResult.Error(FileEditErrorCodes.InvalidPath, $"Invalid file path: {ex.Message}");
        }
    }

    private static bool IsPathWithinRoot(string fullPath, string rootPath)
    {
        var comparison =
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        var normalizedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedFull = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(normalizedFull, normalizedRoot, comparison))
            return true;

        var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedFull.StartsWith(rootWithSeparator, comparison);
    }

    private static async Task<ValidationResult> ValidateFileForEdit(string fullPath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(fullPath);

        // Check file size (default 5MB limit)
        var maxFileSize = GetEnvInt("CODEPUNK_MAX_FILE_SIZE", 5_000_000);
        if (fileInfo.Length > maxFileSize)
        {
            return ValidationResult.Error(FileEditErrorCodes.FileTooLarge,
                $"File too large ({fileInfo.Length} bytes > {maxFileSize} bytes)");
        }

        // Check if binary file
        if (await IsBinaryFile(fullPath, cancellationToken))
        {
            return ValidationResult.Error(FileEditErrorCodes.BinaryFile, "File appears to be binary");
        }

        return ValidationResult.Success();
    }

    private static async Task<bool> IsBinaryFile(string filePath, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(filePath);
        var buffer = new byte[1024];
        var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

        for (int i = 0; i < bytesRead; i++)
        {
            if (buffer[i] == 0) return true; // NULL byte indicates binary
        }

        return false;
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private static int CountOccurrences(string text, string substring)
    {
        if (string.IsNullOrEmpty(substring)) return 0;

        int count = 0;
        int index = 0;

        while ((index = text.IndexOf(substring, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += substring.Length;
        }

        return count;
    }

    private static async Task AtomicWriteAsync(string filePath, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);

        var tempFile = Path.Combine(directory, $".edit_{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(tempFile, content, Encoding.UTF8, cancellationToken);
            File.Move(tempFile, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { /* Ignore cleanup errors */ }
            }
        }
    }

    private static int CalculateTokensSaved(int originalLength, int newLength, int operationCost)
    {
        // Rough estimate: 4 characters per token
        var fullContentTokens = (originalLength + newLength) / 4;
        var operationTokens = operationCost / 4;
        var saved = fullContentTokens - operationTokens;
        return Math.Max(0, saved);
    }

    private static int GetEnvInt(string name, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out var result) && result > 0 ? result : defaultValue;
    }
}
