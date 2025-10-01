using System.Text.Json;
using CodePunk.Core.Services;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models.FileEdit;

namespace CodePunk.Core.Tools;

/// <summary>
/// Tool for replacing exact text in files using literal matching
/// </summary>
public class ReplaceInFileTool : ITool
{
    private readonly IFileEditService _fileEditService;

    public ReplaceInFileTool(IFileEditService fileEditService)
    {
        _fileEditService = fileEditService;
    }

    public string Name => "replace_in_file";
    public string Description => "Replace exact text in a file with new content using literal matching";

    public JsonElement Parameters => JsonDocument.Parse(@"{
        ""type"": ""object"",
        ""properties"": {
            ""file_path"": {
                ""type"": ""string"",
                ""description"": ""The path to the file to modify""
            },
            ""old_string"": {
                ""type"": ""string"",
                ""description"": ""The exact text to replace (include surrounding context for uniqueness)""
            },
            ""new_string"": {
                ""type"": ""string"",
                ""description"": ""The new text to replace the old text with""
            },
            ""expected_occurrences"": {
                ""type"": ""integer"",
                ""description"": ""Expected number of occurrences to validate replacement (optional)""
            },
            ""require_approval"": {
                ""type"": ""boolean"",
                ""description"": ""Whether to require user approval before making changes (default: true)""
            }
        },
        ""required"": [""file_path"", ""old_string"", ""new_string""],
        ""additionalProperties"": false
    }").RootElement;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!arguments.TryGetProperty("file_path", out var pathElement) ||
                !arguments.TryGetProperty("old_string", out var oldStringElement) ||
                !arguments.TryGetProperty("new_string", out var newStringElement))
            {
                return new ToolResult
                {
                    Content = "Missing required parameters: file_path, old_string, and new_string are required",
                    IsError = true,
                    ErrorMessage = "Required parameters are missing"
                };
            }

            var filePath = pathElement.GetString();
            var oldString = oldStringElement.GetString();
            var newString = newStringElement.GetString();

            if (string.IsNullOrEmpty(filePath))
            {
                return new ToolResult
                {
                    Content = "Invalid file path",
                    IsError = true,
                    ErrorMessage = "File path cannot be empty"
                };
            }

            if (string.IsNullOrEmpty(oldString))
            {
                return new ToolResult
                {
                    Content = "Invalid old_string",
                    IsError = true,
                    ErrorMessage = "old_string cannot be empty"
                };
            }

            var expectedOccurrences = arguments.TryGetProperty("expected_occurrences", out var expectedElement)
                ? expectedElement.GetInt32()
                : (int?)null;

            var requireApproval = arguments.TryGetProperty("require_approval", out var approvalElement)
                ? approvalElement.GetBoolean()
                : true;

            var request = new ReplaceRequest(
                filePath,
                oldString,
                newString ?? string.Empty,
                expectedOccurrences,
                requireApproval);

            var result = await _fileEditService.ReplaceInFileAsync(request, cancellationToken);

            if (!result.Success)
            {
                if (result.ErrorCode == "USER_CANCELLED")
                {
                    return new ToolResult
                    {
                        Content = "Operation cancelled by user.",
                        IsError = false,
                        UserCancelled = true
                    };
                }

                return new ToolResult
                {
                    Content = result.ErrorMessage ?? "File replacement failed",
                    IsError = true,
                    ErrorMessage = result.ErrorMessage
                };
            }

            var message = $"Successfully replaced text in {filePath}";
            if (result.Stats != null)
            {
                message += $". Changes: +{result.Stats.LinesAdded}/-{result.Stats.LinesRemoved} lines";
            }
            if (result.TokensSaved.HasValue && result.TokensSaved > 0)
            {
                message += $". Tokens saved (est): {result.TokensSaved}";
            }

            return new ToolResult { Content = message };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                Content = $"Error replacing text in file: {ex.Message}",
                IsError = true,
                ErrorMessage = ex.Message
            };
        }
    }
}