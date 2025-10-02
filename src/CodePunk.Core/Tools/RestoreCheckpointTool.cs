using System.Text;
using System.Text.Json;
using CodePunk.Core.Checkpointing;
using CodePunk.Core.Services;

namespace CodePunk.Core.Tools;

public class RestoreCheckpointTool : ITool
{
    private readonly ICheckpointService _checkpointService;

    public RestoreCheckpointTool(ICheckpointService checkpointService)
    {
        _checkpointService = checkpointService;
    }

    public string Name => "restore_checkpoint";

    public string Description =>
        "Restore project files to a previous checkpoint state. " +
        "Use this to undo changes made by previous tool executions. " +
        "Lists available checkpoints if no ID is provided.";

    public JsonElement Parameters => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            checkpoint_id = new
            {
                type = "string",
                description = "The ID of the checkpoint to restore"
            }
        },
        required = new[] { "checkpoint_id" }
    });

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!arguments.TryGetProperty("checkpoint_id", out var checkpointIdElement))
            {
                return new ToolResult
                {
                    Content = "Missing required parameter: checkpoint_id",
                    IsError = true,
                    ErrorMessage = "checkpoint_id parameter is required"
                };
            }

            var checkpointId = checkpointIdElement.GetString();
            if (string.IsNullOrWhiteSpace(checkpointId))
            {
                return new ToolResult
                {
                    Content = "Invalid checkpoint ID",
                    IsError = true,
                    ErrorMessage = "Checkpoint ID cannot be empty"
                };
            }

            var metadataResult = await _checkpointService.GetCheckpointAsync(checkpointId, cancellationToken);
            if (!metadataResult.Success)
            {
                return new ToolResult
                {
                    Content = metadataResult.ErrorMessage ?? "Checkpoint not found",
                    IsError = true,
                    ErrorMessage = metadataResult.ErrorMessage
                };
            }

            var restoreResult = await _checkpointService.RestoreCheckpointAsync(checkpointId, cancellationToken);
            if (!restoreResult.Success)
            {
                return new ToolResult
                {
                    Content = restoreResult.ErrorMessage ?? "Failed to restore checkpoint",
                    IsError = true,
                    ErrorMessage = restoreResult.ErrorMessage
                };
            }

            var metadata = metadataResult.Data!;
            var response = new StringBuilder();
            response.AppendLine($"Successfully restored checkpoint '{checkpointId}'");
            response.AppendLine($"Tool: {metadata.ToolName}");
            response.AppendLine($"Description: {metadata.Description}");
            response.AppendLine($"Created: {metadata.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");

            if (metadata.ModifiedFiles.Count > 0)
            {
                response.AppendLine($"Files restored: {metadata.ModifiedFiles.Count}");
                response.AppendLine();
                response.AppendLine("Restored files:");
                foreach (var file in metadata.ModifiedFiles.Take(10))
                {
                    response.AppendLine($"  - {file}");
                }

                if (metadata.ModifiedFiles.Count > 10)
                {
                    response.AppendLine($"  ... and {metadata.ModifiedFiles.Count - 10} more");
                }
            }

            return new ToolResult { Content = response.ToString().Trim() };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                Content = $"Error restoring checkpoint: {ex.Message}",
                IsError = true,
                ErrorMessage = ex.Message
            };
        }
    }
}
