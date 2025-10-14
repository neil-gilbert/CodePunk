using System.Text.Json;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using CodePunk.Core.Services;
using CodePunk.Console.Stores;
using Microsoft.Extensions.Options;

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
    private readonly PlanAiGenerationOptions _options;

    public PlanAiGenerationService(IPlanFileStore store, ILLMService llm, IOptions<PlanAiGenerationOptions> opts)
    {
        _store = store;
        _llm = llm;
        _options = opts.Value;
    }
    private static bool TryExtractJsonObject(string input, out string json)
    {
        json = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var s = input.Trim();
        if (s.StartsWith("```"))
        {
            var idx = s.IndexOf('\n');
            if (idx > 0) s = s[(idx + 1)..];
            var endFence = s.LastIndexOf("```", StringComparison.Ordinal);
            if (endFence > 0) s = s[..endFence];
        }
        try
        {
            using var _ = JsonDocument.Parse(s);
            json = s; return true;
        }
        catch { }
        int depth = 0; int start = -1;
        for (int i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch == '{') { if (depth == 0) start = i; depth++; }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    var cand = s.Substring(start, i - start + 1);
                    try { using var _ = JsonDocument.Parse(cand); json = cand; return true; } catch { }
                }
            }
        }
        return false;
    }
    private static bool TryExtractFilesHeuristically(string input, string goal, out List<PlanFileChange> files)
    {
        files = new List<PlanFileChange>();
        if (string.IsNullOrWhiteSpace(input)) return false;
        try
        {
            var rx = new System.Text.RegularExpressions.Regex(@"(?im)\b([A-Za-z0-9_./\\\-]+\.(html|css|js|md|json|yml|yaml|toml))\b");
            var m = rx.Matches(input);
            foreach (System.Text.RegularExpressions.Match match in m)
            {
                var path = match.Groups[1].Value;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    files.Add(new PlanFileChange { Path = path, Rationale = "Heuristic extraction", Generated = true });
                }
            }
            if (files.Count > 0)
            {
                files = files.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                             .Select(g => g.First()).Take(10).ToList();
                return true;
            }
            var lower = (goal + "\n" + input).ToLowerInvariant();
            if (lower.Contains("website") || lower.Contains("static site") || lower.Contains("landing page"))
            {
                files.Add(new PlanFileChange { Path = "public/index.html", Rationale = "Scaffold homepage", Generated = true });
                files.Add(new PlanFileChange { Path = "public/styles.css", Rationale = "Site styling", Generated = true });
                return true;
            }
        }
        catch { }
        return false;
    }

    private static string TruncateUtf8(string value, int maxBytes)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var utf8 = System.Text.Encoding.UTF8;
        var bytes = utf8.GetBytes(value);
        if (bytes.Length <= maxBytes) return value;
        // Ensure we don't cut in middle of multi-byte sequence by walking back
        int len = maxBytes;
        while (len > 0 && (bytes[len - 1] & 0xC0) == 0x80) len--; // continuation byte 10xxxxxx
        if (len <= 0) len = maxBytes; // fallback
        var sliced = utf8.GetString(bytes, 0, len);
        return sliced + "…";
    }

    public async Task<PlanGenerationResult> GenerateAsync(string goal, string? provider, string? model, CancellationToken ct = default)
    {
        // Resolve provider & model
        ILLMProvider? prov = null;
        if (!string.IsNullOrWhiteSpace(provider))
        {
            try { prov = _llm.GetProvider(provider!); } catch { prov = null; }
        }
        if (prov == null)
        {
            try { prov = _llm.GetDefaultProvider(); } catch { prov = null; }
        }
        if (prov == null)
        {
            return new PlanGenerationResult { PlanId = string.Empty, Goal = goal, Provider = provider ?? string.Empty, Model = model ?? string.Empty, Files = new(), ErrorCode = "ModelUnavailable", ErrorMessage = "No LLM provider available" };
        }
        var modelId = model ?? prov.Models?.FirstOrDefault()?.Id ?? "default";
        // Build simple prompt (MVP)
        var systemPrompt = @"You are an AI that generates a JSON plan for multi-file code changes.

CRITICAL: Your entire response must be ONLY a single valid JSON object. Do not include:
- Any explanatory text before the JSON
- Any explanatory text after the JSON
- Any markdown code fences (no ``` )
- Any comments or notes
- Any partial responses

Your response must start with '{' and end with '}' and be valid JSON.

Required JSON format:
{
  ""files"": [
    {
      ""path"": ""relative/path/to/file.ext"",
      ""action"": ""modify"",
      ""rationale"": ""Brief explanation of the change""
    }
  ]
}

Rules:
- Root element MUST be a JSON object (not a string, not an array)
- Must include a ""files"" array property (can be empty array if no changes)
- The ""action"" field must be either ""modify"" or ""delete""
- All paths should be relative (no absolute paths)
- Keep rationale brief and clear
- If uncertain, return at least one file with a sensible path

Example valid response:
{""files"":[{""path"":""README.md"",""action"":""modify"",""rationale"":""Update documentation""}]}";

        var sessionId = "plan-ai-gen"; // ephemeral synthetic session id for prompt construction
        var userPrompt = $@"Goal: {goal}

Generate a JSON plan with the files that need to be changed to accomplish this goal.
Return ONLY the JSON object in the format specified.";

        var messages = new List<Message>
        {
            Message.Create(sessionId, MessageRole.System, new [] { new TextPart(systemPrompt) }),
            Message.Create(sessionId, MessageRole.User, new [] { new TextPart(userPrompt) })
        };
        var req = new LLMRequest { ModelId = modelId, Messages = messages };
    LLMResponse? lastResponse = null;
        List<PlanFileChange> files = new();
        string? lastInvalidMessage = null;
        for (int attempt = 0; attempt <= _options.RetryInvalidOutput; attempt++)
        {
            try
            {
                // Prefer streaming providers when available
                LLMResponse? resp = null;
                if (prov.Models?.Any(m => m.SupportsStreaming) ?? false)
                {
                    var assembler = new CodePunk.Console.Utilities.StreamingJsonAssembler(_options.MaxTotalBytes);
                    try
                    {
                        await foreach (var chunk in prov.StreamAsync(req, ct))
                        {
                            if (!string.IsNullOrEmpty(chunk.Content))
                            {
                                var b = System.Text.Encoding.UTF8.GetBytes(chunk.Content);
                                assembler.Append(b.AsSpan());
                            }
                            if (assembler.TryGetNext(out var el, out var raw, out var diag))
                            {
                                resp = new LLMResponse { Content = raw, Usage = chunk.Usage };
                                break;
                            }
                            if (assembler.HasOverflowed) break;
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch
                    {
                        // ignore streaming errors and fall back to sync
                        resp = null;
                    }
                }
                if (resp == null)
                {
                    resp = await prov.SendAsync(req, ct);
                }
                lastResponse = resp;
            }
            catch (Exception ex)
            {
                return new PlanGenerationResult { PlanId = string.Empty, Goal = goal, Provider = prov.Name, Model = modelId, Files = new(), ErrorCode = "ModelUnavailable", ErrorMessage = ex.Message };
            }
            try
            {
                files.Clear();
                if (lastResponse == null || string.IsNullOrWhiteSpace(lastResponse.Content))
                {
                    lastInvalidMessage = "Empty response from model";
                    throw new JsonException("Empty response");
                }
                var raw = lastResponse.Content;
                bool shouldRetry = false;

                if (!TryExtractJsonObject(raw, out var jsonText))
                {
                    if (TryExtractFilesHeuristically(raw, goal, out var heuristicFiles) && heuristicFiles.Count > 0)
                    {
                        files.AddRange(heuristicFiles);
                        break;
                    }
                    lastInvalidMessage = "Model did not return parseable JSON";
                    shouldRetry = true;
                }
                else
                {
                    using var doc = JsonDocument.Parse(jsonText);
                    // Validate that root element is an object, not a primitive type
                    if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        // Try heuristic extraction as fallback
                        if (TryExtractFilesHeuristically(raw, goal, out var heuristicFiles) && heuristicFiles.Count > 0)
                        {
                            files.AddRange(heuristicFiles);
                            break;
                        }
                        lastInvalidMessage = $"Model returned {doc.RootElement.ValueKind} instead of JSON object";
                        shouldRetry = true;
                    }
                    else if (doc.RootElement.TryGetProperty("files", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in arr.EnumerateArray())
                        {
                            var path = el.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                            var action = el.TryGetProperty("action", out var a) ? a.GetString() ?? "modify" : "modify";
                            var rationale = el.TryGetProperty("rationale", out var r) ? r.GetString() : null;
                            if (string.IsNullOrWhiteSpace(path)) continue;
                            files.Add(new PlanFileChange { Path = path, Rationale = rationale, IsDelete = action == "delete", Generated = true });
                        }
                        // success parse
                        break;
                    }
                }

                // Handle retry if needed (graceful recovery without throwing)
                if (shouldRetry)
                {
                    if (attempt == _options.RetryInvalidOutput)
                    {
                        // Last attempt failed, return error
                        var errorMsg = $"Invalid JSON returned from model: {lastInvalidMessage}";
                        var rawOutput = lastResponse?.Content ?? "";
                        if (!string.IsNullOrWhiteSpace(rawOutput))
                        {
                            var preview = rawOutput.Length > 500 ? rawOutput.Substring(0, 500) + "..." : rawOutput;
                            errorMsg = $"{errorMsg}. Model output: {preview}";
                        }
                        return new PlanGenerationResult
                        {
                            PlanId = string.Empty,
                            Goal = goal,
                            Provider = prov.Name,
                            Model = modelId,
                            Files = new(),
                            RawModelContent = rawOutput,
                            ErrorCode = "ModelOutputInvalid",
                            ErrorMessage = errorMsg
                        };
                    }
                    await Task.Delay(50, ct); // minimal backoff
                    continue;
                }
            }
            catch (Exception ex)
            {
                // Catch actual parsing exceptions (e.g., JsonException, empty response)
                lastInvalidMessage = ex.Message;
                // Try heuristic extraction as last resort
                if (lastResponse != null && !string.IsNullOrWhiteSpace(lastResponse.Content))
                {
                    if (TryExtractFilesHeuristically(lastResponse.Content, goal, out var heuristicFiles) && heuristicFiles.Count > 0)
                    {
                        files.AddRange(heuristicFiles);
                        break;
                    }
                }

                if (attempt == _options.RetryInvalidOutput)
                {
                    var errorMsg = $"Invalid JSON returned from model: {lastInvalidMessage}";
                    var rawOutput = lastResponse?.Content ?? "";
                    if (!string.IsNullOrWhiteSpace(rawOutput))
                    {
                        var preview = rawOutput.Length > 500 ? rawOutput.Substring(0, 500) + "..." : rawOutput;
                        errorMsg = $"{errorMsg}. Model output: {preview}";
                    }
                    return new PlanGenerationResult
                    {
                        PlanId = string.Empty,
                        Goal = goal,
                        Provider = prov.Name,
                        Model = modelId,
                        Files = new(),
                        RawModelContent = rawOutput,
                        ErrorCode = "ModelOutputInvalid",
                        ErrorMessage = errorMsg
                    };
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
        if (files.Count > _options.MaxFiles)
        {
            return new PlanGenerationResult { PlanId = string.Empty, Goal = goal, Provider = prov.Name, Model = modelId, Files = new(), ErrorCode = "TooManyFiles", ErrorMessage = $"File count {files.Count} exceeds limit {_options.MaxFiles}" };
        }
        int aggregateBytes = 0;
        foreach (var f in files.ToList())
        {
            if (f.Path.Length > _options.MaxPathLength || f.Path.Contains("..") || Path.IsPathRooted(f.Path))
            {
                f.Diagnostics ??= new List<string>();
                f.Diagnostics.Add("UnsafePath");
                diagnostics.Add("UnsafePath");
            }
            // Secret scan for rationale (basic)
            if (!string.IsNullOrWhiteSpace(f.Rationale))
            {
                foreach (var pat in _options.SecretPatterns)
                {
                    if (f.Rationale.Contains(pat, StringComparison.OrdinalIgnoreCase))
                    {
                        f.Diagnostics ??= new List<string>();
                        f.Diagnostics.Add("SecretRedacted");
                        f.Rationale = f.Rationale.Replace(pat, "<REDACTED>", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            // Truncation (we only have rationale text currently; future content fields may apply)
            if (!string.IsNullOrEmpty(f.Rationale))
            {
                var bytes = System.Text.Encoding.UTF8.GetByteCount(f.Rationale);
                if (bytes > _options.MaxPerFileBytes)
                {
                    f.Diagnostics ??= new List<string>();
                    f.Diagnostics.Add("TruncatedContent");
                    var truncated = TruncateUtf8(f.Rationale, _options.MaxPerFileBytes);
                    f.Rationale = truncated;
                }
                aggregateBytes += Math.Min(bytes, _options.MaxPerFileBytes);
            }
            if (aggregateBytes > _options.MaxTotalBytes)
            {
                // mark overflow on this and remaining files, then break
                f.Diagnostics ??= new List<string>();
                f.Diagnostics.Add("TruncatedAggregate");
                diagnostics.Add("TruncatedAggregate");
                // Remove any remaining unprocessed files beyond current
                var idx = files.IndexOf(f);
                if (idx < files.Count - 1)
                {
                    files.RemoveRange(idx + 1, files.Count - (idx + 1));
                }
                break;
            }
        }
        // Persist
        var id = await _store.CreateAsync(goal, ct);
        var rec = await _store.GetAsync(id, ct);
        if (rec != null)
        {
            int? promptTokens = null;
            int? completionTokens = null;
            int? totalTokens = null;
            var usage = lastResponse?.Usage;
            if (usage != null)
            {
                promptTokens = usage.InputTokens;
                completionTokens = usage.OutputTokens;
                totalTokens = usage.TotalTokens;
            }
            rec.Generation = new PlanGeneration
            {
                Provider = prov.Name,
                Model = modelId,
                Iterations = 1,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                TotalTokens = totalTokens,
                SafetyFlags = diagnostics.Concat(files.SelectMany(x => x.Diagnostics ?? Enumerable.Empty<string>())).Distinct().ToList()
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
