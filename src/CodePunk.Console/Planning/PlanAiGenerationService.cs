using System.Text.Json;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
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
        var messages = new List<Message>
        {
            new Message("system", systemPrompt),
            new Message("user", $"Goal: {goal}\nReturn a JSON object: {{ files: [ {{ path: 'README.md', action: 'modify', rationale: 'Short note' }} ] }}")
        };
        var req = new LLMRequest { ModelId = modelId, Messages = messages };
        LLMResponse resp;
        try
        {
            resp = await prov.SendAsync(req, ct);
        }
        catch (Exception ex)
        {
            return new PlanGenerationResult { PlanId = string.Empty, Goal = goal, Provider = prov.Name, Model = modelId, Files = new(), ErrorCode = "ModelUnavailable", ErrorMessage = ex.Message };
        }
        // Parse naive JSON (fallback to stub if invalid)
        List<PlanFileChange> files = new();
        try
        {
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
        }
        catch
        {
            return new PlanGenerationResult { PlanId = string.Empty, Goal = goal, Provider = prov.Name, Model = modelId, Files = new(), ErrorCode = "ModelOutputInvalid", ErrorMessage = "Invalid JSON returned from model" };
        }
        if (files.Count == 0)
        {
            files.Add(new PlanFileChange { Path = "README.md", Rationale = "No files parsed; placeholder", Generated = true });
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
                PromptTokens = resp.Usage?.InputTokens,
                CompletionTokens = resp.Usage?.OutputTokens,
                TotalTokens = resp.Usage?.TotalTokens,
                SafetyFlags = new List<string>()
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
            RawModelContent = resp.Content
        };
    }
}
