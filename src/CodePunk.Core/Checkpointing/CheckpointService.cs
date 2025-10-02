using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CodePunk.Core.Checkpointing;

public class CheckpointService : ICheckpointService
{
    private readonly CheckpointOptions _options;
    private readonly GitCommandExecutor _gitExecutor;
    private string? _workspacePath;
    private string? _shadowRepoPath;
    private string? _metadataPath;
    private bool _initialized;

    public CheckpointService(
        IOptions<CheckpointOptions> options,
        GitCommandExecutor gitExecutor)
    {
        _options = options.Value;
        _gitExecutor = gitExecutor;
    }

    public async Task<CheckpointResult> InitializeAsync(
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _workspacePath = Path.GetFullPath(workspacePath);

            var checkpointBase = _options.CheckpointDirectory
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".codepunk",
                    "checkpoints");

            var projectHash = ComputeProjectHash(_workspacePath);
            _shadowRepoPath = Path.Combine(checkpointBase, projectHash);
            _metadataPath = Path.Combine(_shadowRepoPath, "metadata");

            Directory.CreateDirectory(_shadowRepoPath);
            Directory.CreateDirectory(_metadataPath);

            var gitDirExists = Directory.Exists(Path.Combine(_shadowRepoPath, ".git"));
            if (!gitDirExists)
            {
                var initResult = await _gitExecutor.ExecuteAsync(
                    "init",
                    _shadowRepoPath,
                    cancellationToken);

                if (!initResult.Success)
                {
                    return CheckpointResult.Fail(
                        "Failed to initialize git repository",
                        initResult.Error);
                }

                await _gitExecutor.ExecuteAsync(
                    "config user.name \"CodePunk\"",
                    _shadowRepoPath,
                    cancellationToken);

                await _gitExecutor.ExecuteAsync(
                    "config user.email \"codepunk@local\"",
                    _shadowRepoPath,
                    cancellationToken);
            }

            _initialized = true;
            return CheckpointResult.Ok();
        }
        catch (Exception ex)
        {
            return CheckpointResult.Fail(
                "Failed to initialize checkpoint service",
                ex.Message);
        }
    }

    public async Task<CheckpointResult<string>> CreateCheckpointAsync(
        string toolCallId,
        string toolName,
        string description,
        CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            return CheckpointResult<string>.Fail("Service not initialized");
        }

        try
        {
            CopyWorkspaceToShadowRepo();

            var addResult = await _gitExecutor.ExecuteAsync(
                "add -A",
                _shadowRepoPath!,
                cancellationToken);

            if (!addResult.Success)
            {
                return CheckpointResult<string>.Fail(
                    "Failed to stage files",
                    addResult.Error);
            }

            var checkpointId = Guid.NewGuid().ToString("N");
            var commitMessage = $"[{checkpointId}] {toolName}: {description}";

            var commitResult = await _gitExecutor.ExecuteAsync(
                $"commit --allow-empty -m \"{EscapeCommitMessage(commitMessage)}\"",
                _shadowRepoPath!,
                cancellationToken);

            if (!commitResult.Success)
            {
                return CheckpointResult<string>.Fail(
                    "Failed to create commit",
                    commitResult.Error);
            }

            var commitHash = await GetLastCommitHash(cancellationToken);
            var modifiedFiles = await GetModifiedFiles(cancellationToken);

            var metadata = new CheckpointMetadata(
                Id: checkpointId,
                ToolCallId: toolCallId,
                ToolName: toolName,
                Description: description,
                CreatedAt: DateTimeOffset.UtcNow,
                CommitHash: commitHash,
                ModifiedFiles: modifiedFiles);

            await SaveMetadata(metadata, cancellationToken);

            if (_options.AutoPrune)
            {
                await PruneCheckpointsAsync(_options.MaxCheckpoints, cancellationToken);
            }

            return CheckpointResult<string>.Ok(checkpointId);
        }
        catch (Exception ex)
        {
            return CheckpointResult<string>.Fail(
                "Failed to create checkpoint",
                ex.Message);
        }
    }

    public async Task<CheckpointResult> RestoreCheckpointAsync(
        string checkpointId,
        CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            return CheckpointResult.Fail("Service not initialized");
        }

        try
        {
            var metadataResult = await GetCheckpointAsync(checkpointId, cancellationToken);
            if (!metadataResult.Success)
            {
                return CheckpointResult.Fail(metadataResult.ErrorMessage!);
            }

            var commitHash = metadataResult.Data!.CommitHash;

            var checkoutResult = await _gitExecutor.ExecuteAsync(
                $"checkout {commitHash} .",
                _shadowRepoPath!,
                cancellationToken);

            if (!checkoutResult.Success)
            {
                return CheckpointResult.Fail(
                    "Failed to checkout commit",
                    checkoutResult.Error);
            }

            CopyShadowRepoToWorkspace();

            return CheckpointResult.Ok();
        }
        catch (Exception ex)
        {
            return CheckpointResult.Fail(
                "Failed to restore checkpoint",
                ex.Message);
        }
    }

    public async Task<CheckpointResult<IReadOnlyList<CheckpointMetadata>>> ListCheckpointsAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            return CheckpointResult<IReadOnlyList<CheckpointMetadata>>.Fail("Service not initialized");
        }

        try
        {
            var metadataFiles = Directory.GetFiles(_metadataPath!, "*.json")
                .OrderByDescending(File.GetCreationTimeUtc)
                .Take(limit);

            var checkpoints = new List<CheckpointMetadata>();

            foreach (var file in metadataFiles)
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var metadata = JsonSerializer.Deserialize<CheckpointMetadata>(json);
                if (metadata != null)
                {
                    checkpoints.Add(metadata);
                }
            }

            return CheckpointResult<IReadOnlyList<CheckpointMetadata>>.Ok(checkpoints);
        }
        catch (Exception ex)
        {
            return CheckpointResult<IReadOnlyList<CheckpointMetadata>>.Fail(
                "Failed to list checkpoints",
                ex.Message);
        }
    }

    public async Task<CheckpointResult<CheckpointMetadata>> GetCheckpointAsync(
        string checkpointId,
        CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            return CheckpointResult<CheckpointMetadata>.Fail("Service not initialized");
        }

        try
        {
            var metadataFile = Path.Combine(_metadataPath!, $"{checkpointId}.json");

            if (!File.Exists(metadataFile))
            {
                return CheckpointResult<CheckpointMetadata>.Fail(
                    $"Checkpoint '{checkpointId}' not found");
            }

            var json = await File.ReadAllTextAsync(metadataFile, cancellationToken);
            var metadata = JsonSerializer.Deserialize<CheckpointMetadata>(json);

            if (metadata == null)
            {
                return CheckpointResult<CheckpointMetadata>.Fail(
                    "Failed to deserialize checkpoint metadata");
            }

            return CheckpointResult<CheckpointMetadata>.Ok(metadata);
        }
        catch (Exception ex)
        {
            return CheckpointResult<CheckpointMetadata>.Fail(
                "Failed to get checkpoint",
                ex.Message);
        }
    }

    public async Task<CheckpointResult> PruneCheckpointsAsync(
        int keepCount = 100,
        CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            return CheckpointResult.Fail("Service not initialized");
        }

        try
        {
            var metadataFiles = Directory.GetFiles(_metadataPath!, "*.json")
                .OrderByDescending(File.GetCreationTimeUtc)
                .ToList();

            if (metadataFiles.Count <= keepCount)
            {
                return CheckpointResult.Ok();
            }

            var filesToDelete = metadataFiles.Skip(keepCount);

            foreach (var file in filesToDelete)
            {
                File.Delete(file);
            }

            return CheckpointResult.Ok();
        }
        catch (Exception ex)
        {
            return CheckpointResult.Fail(
                "Failed to prune checkpoints",
                ex.Message);
        }
    }

    private void CopyWorkspaceToShadowRepo()
    {
        var entries = Directory.GetFileSystemEntries(_workspacePath!, "*", SearchOption.AllDirectories)
            .Where(p => !p.Contains(".git") && !p.Contains(".codepunk"));

        foreach (var entry in entries)
        {
            var relativePath = Path.GetRelativePath(_workspacePath!, entry);
            var targetPath = Path.Combine(_shadowRepoPath!, relativePath);

            if (Directory.Exists(entry))
            {
                Directory.CreateDirectory(targetPath);
            }
            else if (File.Exists(entry))
            {
                var targetDir = Path.GetDirectoryName(targetPath)!;
                Directory.CreateDirectory(targetDir);
                File.Copy(entry, targetPath, overwrite: true);
            }
        }
    }

    private void CopyShadowRepoToWorkspace()
    {
        var entries = Directory.GetFileSystemEntries(_shadowRepoPath!, "*", SearchOption.AllDirectories)
            .Where(p => !p.Contains(".git") && !p.Contains("metadata"));

        foreach (var entry in entries)
        {
            var relativePath = Path.GetRelativePath(_shadowRepoPath!, entry);
            var targetPath = Path.Combine(_workspacePath!, relativePath);

            if (Directory.Exists(entry))
            {
                Directory.CreateDirectory(targetPath);
            }
            else if (File.Exists(entry))
            {
                var targetDir = Path.GetDirectoryName(targetPath)!;
                Directory.CreateDirectory(targetDir);
                File.Copy(entry, targetPath, overwrite: true);
            }
        }
    }

    private async Task<string> GetLastCommitHash(CancellationToken cancellationToken)
    {
        var result = await _gitExecutor.ExecuteAsync(
            "rev-parse HEAD",
            _shadowRepoPath!,
            cancellationToken);

        return result.Success ? result.Output.Trim() : string.Empty;
    }

    private async Task<IReadOnlyList<string>> GetModifiedFiles(CancellationToken cancellationToken)
    {
        var result = await _gitExecutor.ExecuteAsync(
            "show --name-only --pretty=format: HEAD",
            _shadowRepoPath!,
            cancellationToken);

        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
        {
            return Array.Empty<string>();
        }

        return result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .ToList();
    }

    private async Task SaveMetadata(CheckpointMetadata metadata, CancellationToken cancellationToken)
    {
        var metadataFile = Path.Combine(_metadataPath!, $"{metadata.Id}.json");
        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(metadataFile, json, cancellationToken);
    }

    private static string ComputeProjectHash(string path)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(path);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static string EscapeCommitMessage(string message)
    {
        return message.Replace("\"", "\\\"");
    }

}
