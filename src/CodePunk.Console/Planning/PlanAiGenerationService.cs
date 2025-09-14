using System.Text.Json;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using CodePunk.Core.Services;
using CodePunk.Console.Stores;

namespace CodePunk.Console.Planning;

public interface IPlanAiGenerationService
{
    Task<PlanGenerationResult> GenerateAsync(string goal, string? provider, string? model, CancellationToken ct = default);
}

public class PlanGenerationResult
{
    public required string PlanId { get; init; }
    public required string Goal { get; init; }
    public required string Provider { get; init; }
    public required string Model { get; init; }
    public required List<PlanFileChange> Files { get; init; }
    public PlanGeneration? Generation { get; init; }
    public string? RawModelContent { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

internal class PlanAiGenerationService : IPlanAiGenerationService
{
    private readonly IPlanFileStore _store;
    private readonly ILLMService _llm;
    private const int MaxFilesDefault = 20;
    private const int MaxPathLength = 260;
    private static readonly string[] SecretPatterns = new [] { "API_KEY=", "SECRET=", "PASSWORD=", "-----BEGIN" };
    private const int RetryInvalidOutput = 1; // retries beyond first attempt

    public PlanAiGenerationService(IPlanFileStore store, ILLMService llm)
    {
        _store = store;
        _llm = llm;
    }

    public async Task<PlanGenerationResult> GenerateAsync(string goal, string? provider, string? model, CancellationToken ct = default)
    {
        // Resolve provider & model
        ILLMProvider prov;
        if (!string.IsNullOrWhiteSpace(provider))
        {
            prov = _llm.GetProvider(provider!) ?? _llm.GetDefaultProvider();
        }
        else
        {
            prov = _llm.GetDefaultProvider();
        }
        var modelId = model ?? prov.Models.FirstOrDefault()?.Id ?? "default";
        // Build simple prompt (MVP)
        var systemPrompt = "You are an AI that outputs a JSON plan for multi-file code changes. Return JSON only.";
        var sessionId = "plan-ai-gen"; // ephemeral synthetic session id for prompt construction
        var messages = new List<Message>
        {
            Message.Create(sessionId, MessageRole.System, new [] { new TextPart(systemPrompt) }),
            Message.Create(sessionId, MessageRole.User, new [] { new TextPart($"Goal: {goal}\nReturn a JSON object: {{ files: [ {{ path: 'README.md', action: 'modify', rationale: 'Short note' }} ] }}") })
        };
        var req = new LLMRequest { ModelId = modelId, Messages = messages };
        LLMResponse resp;
        List<PlanFileChange> files = new();
        string? lastInvalidMessage = null;
        for (int attempt = 0; attempt <= RetryInvalidOutput; attempt++)
        {
            try
            {
                resp = await prov.SendAsync(req, ct);
            }
            catch (Exception ex)
            {
                return new PlanGenerationResult { PlanId = string.Empty, Goal = goal, Provider = prov.Name, Model = modelId, Files = new(), ErrorCode = "ModelUnavailable", ErrorMessage = ex.Message };
            }
            try
            {
                files.Clear();
                using var doc = JsonDocument.Parse(resp.Content);
                if (doc.RootElement.TryGetProperty("files", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in arr.EnumerateArray())
                    {
                        var path = el.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                        var action = el.TryGetProperty("action", out var a) ? a.GetString() ?? "modify" : "modify";
                        var rationale = el.TryGetProperty("rationale", out var r) ? r.GetString() : null;
                        if (string.IsNullOrWhiteSpace(path)) continue;
                        files.Add(new PlanFileChange { Path = path, Rationale = rationale, IsDelete = action == "delete", Generated = true });
                    }
                }
                // success parse
                break;
            }
            catch (Exception ex)
            {
                lastInvalidMessage = ex.Message;
                if (attempt == RetryInvalidOutput)
                {
                    return new PlanGenerationResult { PlanId = string.Empty, Goal = goal, Provider = prov.Name, Model = modelId, Files = new(), ErrorCode = "ModelOutputInvalid", ErrorMessage = "Invalid JSON returned from model" };
                }
                await Task.Delay(50, ct); // minimal backoff
                continue;
            }
        }
        if (files.Count == 0)
        {
            files.Add(new PlanFileChange { Path = "README.md", Rationale = "No files parsed; placeholder", Generated = true });
        }
        // Safety validation
        var diagnostics = new List<string>();
        if (files.Count > MaxFilesDefault)
        {
            return new PlanGenerationResult { PlanId = string.Empty, Goal = goal, Provider = prov.Name, Model = modelId, Files = new(), ErrorCode = "TooManyFiles", ErrorMessage = $"File count {files.Count} exceeds limit {MaxFilesDefault}" };
        }
        foreach (var f in files.ToList())
        {
            if (f.Path.Length > MaxPathLength || f.Path.Contains("..") || Path.IsPathRooted(f.Path))
            {
                f.Diagnostics ??= new List<string>();
                f.Diagnostics.Add("UnsafePath");
                diagnostics.Add("UnsafePath");
            }
            // Secret scan for rationale (basic)
            if (!string.IsNullOrWhiteSpace(f.Rationale))
            {
                foreach (var pat in SecretPatterns)
                {
                    if (f.Rationale.Contains(pat, StringComparison.OrdinalIgnoreCase))
                    {
                        f.Diagnostics ??= new List<string>();
                        f.Diagnostics.Add("SecretRedacted");
                        f.Rationale = f.Rationale.Replace(pat, "<REDACTED>", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
        }
        // Persist
        var id = await _store.CreateAsync(goal, ct);
        var rec = await _store.GetAsync(id, ct);
        if (rec != null)
        {
            rec.Generation = new PlanGeneration
            {
                Provider = prov.Name,
                Model = modelId,
                Iterations = 1,
                PromptTokens = null,
                CompletionTokens = null,
                TotalTokens = null,
                SafetyFlags = diagnostics.Distinct().ToList()
            };
            rec.Files.AddRange(files);
            await _store.SaveAsync(rec, ct);
        }
        return new PlanGenerationResult
        {
            PlanId = id,
            Goal = goal,
            Provider = prov.Name,
            Model = modelId,
            Files = files,
            Generation = rec?.Generation,
            RawModelContent = null
        };
    }
}
