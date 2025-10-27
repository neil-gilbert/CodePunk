using System.Globalization;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace CodePunk.Core.Providers.Anthropic;

/// <summary>
/// Anthropic provider using direct HTTP and SSE to the Messages API.
/// </summary>
public class AnthropicProvider : ILLMProvider
{
    private readonly HttpClient _httpClient;
    private readonly AnthropicConfiguration _config;
    private readonly ILogger<AnthropicProvider> _logger;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;
    private readonly string? _ephemeralTtl;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public string Name => "Anthropic";

    public IReadOnlyList<LLMModel> Models { get; }

    public AnthropicProvider(HttpClient httpClient, AnthropicConfiguration config, ILogger<AnthropicProvider> logger)
        : this(httpClient, config, new Core.Caching.PromptCacheOptions(), logger)
    {
    }

    public AnthropicProvider(HttpClient httpClient, AnthropicConfiguration config, Core.Caching.PromptCacheOptions cacheOptions, ILogger<AnthropicProvider> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
        _retryPolicy = CreateRetryPolicy(_logger);

        _ephemeralTtl = MapEphemeralTtl(cacheOptions.DefaultTtl);

        var baseUrl = config.BaseUrl.TrimEnd('/') + "/";
        _httpClient.BaseAddress = new Uri(baseUrl);
        var apiKey = (config.ApiKey ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
        var version = (config.Version ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
        _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        if (!string.IsNullOrEmpty(version))
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", version);
        _httpClient.Timeout = config.Timeout;

        Models = CreateModels();
    }

    /// <summary>
    /// Attempt to fetch live models from Anthropic. Currently returns static list.
    /// </summary>
    public Task<IReadOnlyList<LLMModel>> FetchModelsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Models ?? Array.Empty<LLMModel>());
    }

    public async Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var anthropicRequest = ConvertToAnthropicRequest(request);

            _logger.LogInformation("Anthropic request: Tools count={ToolCount}", anthropicRequest.Tools?.Count ?? 0);
            if (anthropicRequest.Tools?.Any() == true)
            {
                _logger.LogInformation("Anthropic request tools: {Tools}", string.Join(", ", anthropicRequest.Tools.Select(t => t.Name)));
            }

            _logger.LogDebug("Sending request to Anthropic API for model {Model}", request.ModelId);
            HttpResponseMessage? response = null;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                response = await _httpClient.PostAsJsonAsync("messages", anthropicRequest, JsonOptions, cancellationToken);
                if (response.IsSuccessStatusCode) break;
                if ((int)response.StatusCode == 429 || (int)response.StatusCode == 503)
                {
                    var retryAfter = GetRetryAfterDelay(response);
                    if (retryAfter.HasValue)
                    {
                        if (retryAfter.Value > TimeSpan.Zero)
                            await Task.Delay(retryAfter.Value, cancellationToken);
                        continue;
                    }
                    var delay = GetBackoffWithJitter(attempt);
                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay, cancellationToken);
                    continue;
                }
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw CreateApiException(response, errorBody);
            }

            if (response == null)
                throw new InvalidOperationException("No response from Anthropic API");

            LogRateLimits(response);

            var anthropicResponse = await response.Content.ReadFromJsonAsync<AnthropicResponse>(JsonOptions, cancellationToken);

            if (anthropicResponse == null)
            {
                throw new InvalidOperationException("Received null response from Anthropic API");
            }

            return ConvertFromAnthropicResponse(anthropicResponse);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error calling Anthropic API");
            throw new InvalidOperationException("Failed to call Anthropic API", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Anthropic API call timed out");
            throw new InvalidOperationException("Anthropic API call timed out", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error calling Anthropic API");
            throw;
        }
    }

    public async IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var anthropicRequest = ConvertToAnthropicRequest(request);

        _logger.LogInformation("Anthropic streaming request: Tools count={ToolCount}", anthropicRequest.Tools?.Count ?? 0);
        if (anthropicRequest.Tools?.Any() == true)
        {
            _logger.LogInformation("Anthropic streaming request tools: {Tools}", string.Join(", ", anthropicRequest.Tools.Select(t => t.Name)));
        }

        anthropicRequest.Stream = true;

        _logger.LogDebug("Starting streaming request to Anthropic API for model {Model}", request.ModelId);

        HttpResponseMessage? streamResponseMsg = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            // Create a fresh HttpRequestMessage for each retry attempt
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "messages")
            {
                Content = JsonContent.Create(anthropicRequest, options: JsonOptions)
            };

            httpRequest.Headers.Accept.Clear();
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            streamResponseMsg = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (streamResponseMsg.IsSuccessStatusCode) break;
            if ((int)streamResponseMsg.StatusCode == 429 || (int)streamResponseMsg.StatusCode == 503)
            {
                var retryAfter = GetRetryAfterDelay(streamResponseMsg);
                if (retryAfter.HasValue)
                {
                    if (retryAfter.Value > TimeSpan.Zero)
                        await Task.Delay(retryAfter.Value, cancellationToken);
                    continue;
                }
                var delay = GetBackoffWithJitter(attempt);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken);
                continue;
            }
            var err = await streamResponseMsg.Content.ReadAsStringAsync(cancellationToken);
            throw CreateApiException(streamResponseMsg, err);
        }
        if (streamResponseMsg == null)
            yield break;

        LogRateLimits(streamResponseMsg);

        using var stream = await streamResponseMsg.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        _logger.LogInformation("Starting to read Anthropic streaming response");

        LLMUsage? tokenUsage = null;
        var toolCallBuilders = new Dictionary<int, (ToolCall toolCall, StringBuilder jsonBuilder, string type)>();
        var cacheInfoEmitted = false;

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);

            if (string.IsNullOrEmpty(line))
                continue;

            var trimmed = line.Trim();
            if (trimmed.Length > 0 && trimmed[0] == '\uFEFF')
            {
                trimmed = trimmed.TrimStart('\uFEFF');
            }
            if (trimmed.StartsWith("event: "))
            {
                continue;
            }

            if (trimmed.StartsWith("data:") || trimmed.StartsWith("{") || trimmed.Contains("\"type\":"))
            {
                string jsonData = trimmed;
                var dataIndex = trimmed.IndexOf("data:");
                if (dataIndex >= 0)
                {
                    jsonData = trimmed.Substring(dataIndex + 5).TrimStart();
                }

                if (jsonData == "[DONE]")
                    break;

                AnthropicStreamResponse? streamResponse;
                try
                {
                    streamResponse = JsonSerializer.Deserialize<AnthropicStreamResponse>(jsonData, JsonOptions);
                }
                catch
                {
                    continue;
                }

                if (streamResponse == null) continue;

                if (!cacheInfoEmitted)
                {
                    var cacheInfo = ConvertCacheControl(streamResponse.Message?.CacheControl)
                        ?? ConvertCacheControl(streamResponse.ContentBlock?.CacheControl);

                    if (cacheInfo != null)
                    {
                        cacheInfoEmitted = true;
                        yield return new LLMStreamChunk
                        {
                            PromptCache = cacheInfo,
                            IsComplete = false
                        };
                    }
                }

                switch (streamResponse.Type)
                {
                    case "message_start":
                        if (streamResponse.Message?.Usage != null)
                        {
                            tokenUsage = new LLMUsage { InputTokens = streamResponse.Message.Usage.InputTokens };
                        }
                        break;

                    case "content_block_start":
                        if (streamResponse.ContentBlock != null && streamResponse.Index.HasValue)
                        {
                            var t = streamResponse.ContentBlock.Type;
                            if (t == "tool_use" || t == "server_tool_use" || t == "mcp_tool_use")
                            {
                                var toolCall = new ToolCall
                                {
                                    Id = streamResponse.ContentBlock.Id ?? string.Empty,
                                    Name = streamResponse.ContentBlock.Name ?? string.Empty,
                                };
                                toolCallBuilders[streamResponse.Index.Value] = (toolCall, new StringBuilder(), t);
                            }
                            else if (t == "web_search_tool_result" || t == "web_search_tool_result_error")
                            {
                                yield return new LLMStreamChunk
                                {
                                    WebSearchToolResult = new WebSearchToolResultEvent(
                                        streamResponse.ContentBlock.ToolUseId,
                                        streamResponse.ContentBlock.IsError,
                                        streamResponse.ContentBlock.Content
                                    ),
                                    EventType = t,
                                    IsComplete = false
                                };
                            }
                        }
                        break;

                    case "content_block_delta":
                        if (streamResponse.Index.HasValue && toolCallBuilders.TryGetValue(streamResponse.Index.Value, out var builder))
                        {
                            if (streamResponse.Delta.ValueKind == JsonValueKind.Object &&
                                streamResponse.Delta.TryGetProperty("type", out var deltaType) &&
                                deltaType.GetString() == "input_json_delta" &&
                                streamResponse.Delta.TryGetProperty("partial_json", out var partialJson))
                            {
                                builder.jsonBuilder.Append(partialJson.GetString());
                            }
                        }
                        if (streamResponse.Delta.ValueKind == JsonValueKind.Object &&
                            streamResponse.Delta.TryGetProperty("type", out var deltaType2) &&
                            deltaType2.GetString() == "text_delta" &&
                            streamResponse.Delta.TryGetProperty("text", out var textDelta))
                        {
                            yield return new LLMStreamChunk
                            {
                                Content = textDelta.GetString(),
                                IsComplete = false
                            };
                        }
                        break;

                    case "content_block_stop":
                        if (streamResponse.Index.HasValue && toolCallBuilders.Remove(streamResponse.Index.Value, out var finishedBuilder))
                        {
                            var (toolCall, jsonBuilder, t) = finishedBuilder;
                            var finalJson = jsonBuilder.ToString();
                            LLMStreamChunk? pending = null;
                            try
                            {
                                if (t == "tool_use")
                                {
                                    var arguments = JsonSerializer.Deserialize<JsonElement>(finalJson);
                                    pending = new LLMStreamChunk
                                    {
                                        ToolCall = new ToolCall
                                        {
                                            Id = toolCall.Id,
                                            Name = toolCall.Name,
                                            Arguments = arguments
                                        },
                                        IsComplete = false,
                                        EventType = t
                                    };
                                }
                                else if (t == "server_tool_use")
                                {
                                    string? query = null;
                                    // Attempt robust extraction: JSON parse with fallback string search
                                    try
                                    {
                                        using var jd = JsonDocument.Parse(finalJson);
                                        if (jd.RootElement.TryGetProperty("query", out var q1)) query = q1.GetString();
                                        if (string.IsNullOrEmpty(query) && jd.RootElement.TryGetProperty("q", out var q2)) query = q2.GetString();
                                    }
                                    catch 
                                    {
                                        var marker = "\"q\":\"";
                                        var idx = finalJson.IndexOf(marker, StringComparison.Ordinal);
                                        if (idx >= 0)
                                        {
                                            var start = idx + marker.Length;
                                            var end = finalJson.IndexOf('"', start);
                                            if (end > start) query = finalJson.Substring(start, end - start);
                                        }
                                    }
                                    pending = new LLMStreamChunk
                                    {
                                        ServerToolUse = new ServerToolUseEvent(toolCall.Id, toolCall.Name, query),
                                        EventType = t,
                                        IsComplete = false
                                    };
                                }
                                else if (t == "mcp_tool_use")
                                {
                                    JsonElement input;
                                    try { input = JsonDocument.Parse(finalJson).RootElement.Clone(); }
                                    catch { input = JsonDocument.Parse("{}").RootElement.Clone(); }
                                    pending = new LLMStreamChunk
                                    {
                                        McpToolUse = new McpToolUseEvent(toolCall.Id, toolCall.Name, streamResponse.ContentBlock?.ServerName, input),
                                        EventType = t,
                                        IsComplete = false
                                    };
                                }
                            }
                            catch { }
                            if (pending == null)
                            {
                                if (t == "server_tool_use")
                                {
                                    pending = new LLMStreamChunk
                                    {
                                        ServerToolUse = new ServerToolUseEvent(toolCall.Id, toolCall.Name, null),
                                        EventType = t,
                                        IsComplete = false
                                    };
                                }
                                else if (t == "mcp_tool_use")
                                {
                                    var empty = JsonDocument.Parse("{}").RootElement.Clone();
                                    pending = new LLMStreamChunk
                                    {
                                        McpToolUse = new McpToolUseEvent(toolCall.Id, toolCall.Name, streamResponse.ContentBlock?.ServerName, empty),
                                        EventType = t,
                                        IsComplete = false
                                    };
                                }
                            }
                            if (pending != null)
                            {
                                yield return pending;
                            }
                        }
                        break;

                    case "message_delta":
                        if (streamResponse.Delta.ValueKind == JsonValueKind.Object &&
                            streamResponse.Delta.TryGetProperty("stop_reason", out var stopReason))
                        {
                            LLMUsage? finalUsage = null;
                            if (streamResponse.Usage != null)
                            {
                                finalUsage = new LLMUsage
                                {
                                    InputTokens = tokenUsage?.InputTokens ?? 0,
                                    OutputTokens = streamResponse.Usage.OutputTokens
                                };
                            }
                            yield return new LLMStreamChunk
                            {
                                Usage = finalUsage,
                                IsComplete = true,
                                FinishReason = ConvertFinishReason(stopReason.GetString())
                            };
                        }
                        else if (streamResponse.Delta.ValueKind == JsonValueKind.Object &&
                                 streamResponse.Delta.TryGetProperty("text", out var text))
                        {
                            yield return new LLMStreamChunk
                            {
                                Content = text.GetString(),
                                IsComplete = false
                            };
                        }
                        break;

                    case "message_stop":
                        LLMUsage? completeUsage = null;
                        if (streamResponse.Usage != null)
                        {
                            completeUsage = new LLMUsage
                            {
                                InputTokens = tokenUsage?.InputTokens ?? streamResponse.Usage.InputTokens,
                                OutputTokens = streamResponse.Usage.OutputTokens
                            };
                        }
                        else if (tokenUsage != null)
                        {
                            completeUsage = tokenUsage;
                        }

                        yield return new LLMStreamChunk
                        {
                            Usage = completeUsage,
                            IsComplete = true,
                            FinishReason = LLMFinishReason.Stop
                        };
                        break;
                }
            }
        }

        _logger.LogInformation("Finished reading Anthropic streaming response");
    }

    /// <summary>
    /// Counts message input tokens for a request using the provider endpoint.
    /// </summary>
    public async Task<int> CountTokensAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        var req = ConvertToAnthropicRequest(request);
        var payload = new
        {
            model = req.Model,
            messages = req.Messages,
            system = req.System,
            tools = req.Tools
        };
        HttpResponseMessage? resp = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            resp = await _httpClient.PostAsJsonAsync("messages/count_tokens", payload, JsonOptions, cancellationToken);
            if (resp.IsSuccessStatusCode) break;
            if ((int)resp.StatusCode == 429 || (int)resp.StatusCode == 503)
            {
                var delay = GetRetryAfterDelay(resp) ?? GetBackoffWithJitter(attempt);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }
            }
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Anthropic count_tokens error {Status}: {Body}", (int)resp.StatusCode, body);
            resp.EnsureSuccessStatusCode();
        }
        if (resp == null) return 0;
        var parsed = await resp.Content.ReadFromJsonAsync<CountTokensResponse>(JsonOptions, cancellationToken);
        return parsed?.InputTokens ?? 0;
    }

    private sealed class CountTokensResponse
    {
        public int InputTokens { get; set; }
    }

    private static AsyncRetryPolicy<HttpResponseMessage> CreateRetryPolicy(ILogger logger)
    {
        var sleepDurations = new[] { 0.5, 1.0, 2.0, 4.0 };
        var rnd = new Random();
        return Policy
            .HandleResult<HttpResponseMessage>(r => (int)r.StatusCode == 429 || (int)r.StatusCode == 503)
            .WaitAndRetryAsync(
                sleepDurations.Length,
                retryAttempt =>
                {
                    var baseDelay = TimeSpan.FromSeconds(sleepDurations[retryAttempt - 1]);
                    var jitter = TimeSpan.FromMilliseconds(rnd.Next(50, 250));
                    var delay = baseDelay + jitter;
                    logger.LogDebug("Anthropic transient HTTP issue – retry {Attempt} in {Delay}.", retryAttempt, delay);
                    return delay;
                });
    }

    private static TimeSpan? GetRetryAfterDelay(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            var v = values.FirstOrDefault();
            if (int.TryParse(v, out var seconds)) return TimeSpan.FromSeconds(seconds);
            if (DateTimeOffset.TryParse(v, out var at))
            {
                var delta = at - DateTimeOffset.UtcNow;
                if (delta > TimeSpan.Zero) return delta;
            }
        }
        return null;
    }

    private static TimeSpan GetBackoffWithJitter(int attempt)
    {
        var steps = new[] { 0.5, 1.0, 2.0, 4.0 };
        var idx = Math.Min(attempt, steps.Length - 1);
        var rnd = new Random();
        return TimeSpan.FromSeconds(steps[idx]) + TimeSpan.FromMilliseconds(rnd.Next(50, 250));
    }

    private void LogRateLimits(HttpResponseMessage response)
    {
        try
        {
            var headers = response.Headers;
            string Get(string name) => headers.TryGetValues(name, out var vs) ? vs.FirstOrDefault() ?? string.Empty : string.Empty;
            var reqLimit = Get("x-ratelimit-requests-limit");
            var reqRemain = Get("x-ratelimit-requests-remaining");
            var tokLimit = Get("x-ratelimit-tokens-limit");
            var tokRemain = Get("x-ratelimit-tokens-remaining");
            _logger.LogDebug("Anthropic rate limits: req {ReqRemain}/{ReqLimit}, tok {TokRemain}/{TokLimit}", reqRemain, reqLimit, tokRemain, tokLimit);
        }
        catch { }
    }

    private Exception CreateApiException(HttpResponseMessage response, string body)
    {
        var code = (int)response.StatusCode;
        if (code == 401)
        {
            return new InvalidOperationException("Anthropic unauthorized. Check API key.");
        }
        if (code == 429)
        {
            var retry = GetRetryAfterDelay(response);
            var msg = retry.HasValue ? $"Rate limit exceeded. Retry after {retry.Value.TotalSeconds:F0}s." : "Rate limit exceeded.";
            return new InvalidOperationException(msg);
        }
        if (code >= 500)
        {
            return new InvalidOperationException("Anthropic server error. Please retry.");
        }
        var snippet = string.Empty;
        try { snippet = body?.Length > 300 ? body.Substring(0, 300) + "…" : body ?? string.Empty; } catch { }
        return new InvalidOperationException($"Anthropic error {(int)response.StatusCode}: {snippet}");
    }

    private AnthropicRequest ConvertToAnthropicRequest(LLMRequest request)
    {
        var (messages, systemPrompt) = ConvertMessages(request.Messages, request.SystemPrompt);
        var tools = ConvertTools(request.Tools, request.UseEphemeralCache);
        var systemContent = BuildSystemContent(request, systemPrompt);

        return new AnthropicRequest
        {
            Model = request.ModelId,
            MaxTokens = request.MaxTokens,
            Temperature = request.Temperature,
            System = systemContent,
            Messages = messages,
            Tools = tools?.Count > 0 ? tools : null
        };
    }

    private List<AnthropicTool>? ConvertTools(IReadOnlyList<LLMTool>? tools, bool useEphemeral)
    {
        if (tools == null || tools.Count == 0)
            return null;

        var anthropicTools = new List<AnthropicTool>();
        foreach (var tool in tools)
        {
            anthropicTools.Add(new AnthropicTool
            {
                Name = tool.Name,
                Description = tool.Description,
                InputSchema = tool.Parameters
            });
        }
        if (useEphemeral && anthropicTools.Count > 0)
        {
            anthropicTools[^1].CacheControl = new AnthropicCacheControlRequest { Type = "ephemeral", Ttl = _ephemeralTtl };
        }
        return anthropicTools;
    }

    private List<AnthropicSystemContent>? BuildSystemContent(LLMRequest request, string? systemPrompt)
    {
        if (string.IsNullOrWhiteSpace(systemPrompt) && request.ResponseFormat == null)
        {
            return null;
        }

        var list = new List<AnthropicSystemContent>();

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            var entry = new AnthropicSystemContent
            {
                Type = "text",
                Text = systemPrompt
            };
            if (request.UseEphemeralCache)
            {
                entry.CacheControl = new AnthropicCacheControlRequest { Type = "ephemeral", Ttl = _ephemeralTtl };
            }
            list.Add(entry);
        }

        // Structured-output guidance: ask for strict JSON only; include schema when available
        if (request.ResponseFormat is { } rf)
        {
            var guidance = rf.Type.ToLowerInvariant() switch
            {
                "json_schema" => BuildJsonSchemaGuidance(rf),
                "json_object" => "Respond with a single valid JSON object only. No explanations, no markdown, no prose.",
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(guidance))
            {
                var guidanceEntry = new AnthropicSystemContent
                {
                    Type = "text",
                    Text = guidance
                };
                if (request.UseEphemeralCache)
                {
                    guidanceEntry.CacheControl = new AnthropicCacheControlRequest { Type = "ephemeral", Ttl = _ephemeralTtl };
                }
                list.Add(guidanceEntry);
            }
        }

        return list.Count > 0 ? list : null;
    }

    private static string BuildJsonSchemaGuidance(LLMResponseFormat rf)
    {
        try
        {
            var schemaText = rf.JsonSchema.HasValue ? rf.JsonSchema.Value.GetRawText() : "{}";
            var name = string.IsNullOrWhiteSpace(rf.SchemaName) ? "Response" : rf.SchemaName;
            return "You must output only a single valid JSON object that strictly conforms to the following JSON Schema. " +
                   "Do not include any explanations, markdown, or additional text.\n" +
                   $"Schema name: {name}\n" +
                   schemaText;
        }
        catch
        {
            return "You must output only a single valid JSON object. Do not include explanations, markdown, or additional text.";
        }
    }

    private (List<AnthropicMessage>, string?) ConvertMessages(IReadOnlyList<Message> messages, string? systemPrompt)
    {
        var anthropicMessages = new List<AnthropicMessage>();
        string? extractedSystemPrompt = systemPrompt;

        foreach (var message in messages)
        {
            var role = message.Role switch
            {
                MessageRole.System => "system",
                MessageRole.User => "user",
                MessageRole.Assistant => "assistant",
                MessageRole.Tool => "user",
                _ => null
            };

            if (role == null) continue;

            if (role == "system")
            {
                var textPart = message.Parts.OfType<TextPart>().FirstOrDefault();
                if (textPart != null)
                {
                    extractedSystemPrompt = string.IsNullOrEmpty(extractedSystemPrompt)
                        ? textPart.Content
                        : $"{extractedSystemPrompt}\n\n{textPart.Content}";
                }
                continue;
            }

            var contentParts = new List<object>();
            var textContent = string.Join("\n", message.Parts.OfType<TextPart>().Select(p => p.Content));
            if (!string.IsNullOrEmpty(textContent))
            {
                contentParts.Add(new AnthropicTextContent { Text = textContent });
            }

            var toolCallParts = message.Parts.OfType<ToolCallPart>().ToList();
            if (toolCallParts.Any())
            {
                foreach (var part in toolCallParts)
                {
                    contentParts.Add(new AnthropicToolUseContent
                    {
                        Id = part.Id,
                        Name = part.Name,
                        Input = part.Arguments
                    });
                }
            }

            var toolResultParts = message.Parts.OfType<ToolResultPart>().ToList();
            if (toolResultParts.Any())
            {
                role = "user";
                foreach (var part in toolResultParts)
                {
                    contentParts.Add(new AnthropicToolResultContent
                    {
                        Type = "tool_result",
                        ToolUseId = part.ToolCallId,
                        Content = part.Content,
                        IsError = part.IsError
                    });
                }
            }

            if (contentParts.Count > 0)
            {
                if (message.Role == MessageRole.Assistant)
                {
                    role = "assistant";
                }
                anthropicMessages.Add(new AnthropicMessage
                {
                    Role = role,
                    Content = contentParts
                });
            }
        }

        return (anthropicMessages, extractedSystemPrompt);
    }

    private string GetMessageContent(Message message)
    {
        var textParts = message.Parts.OfType<TextPart>();
        return string.Join("\n", textParts.Select(p => p.Content));
    }

    private LLMResponse ConvertFromAnthropicResponse(AnthropicResponse response)
    {
        var content = response.Content.FirstOrDefault(c => c.Type == "text")?.Text ?? string.Empty;

        var toolCalls = response.Content
            .Where(c => c.Type == "tool_use")
            .Select(c => new ToolCall
            {
                Id = c.Id ?? string.Empty,
                Name = c.Name ?? string.Empty,
                Arguments = JsonSerializer.SerializeToElement(c.Input ?? new object())
            })
            .ToList();

        _logger.LogInformation("Anthropic response: Content blocks={ContentBlocks}, Tool calls found={ToolCallCount}", response.Content.Count, toolCalls.Count);

        var cacheInfo = ConvertCacheControl(response.CacheControl)
            ?? response.Content.Select(c => ConvertCacheControl(c.CacheControl)).FirstOrDefault(info => info != null);

        return new LLMResponse
        {
            Content = content,
            ToolCalls = toolCalls.Any() ? toolCalls : null,
            Usage = response.Usage != null ? new LLMUsage
            {
                InputTokens = response.Usage.InputTokens,
                OutputTokens = response.Usage.OutputTokens
            } : null,
            FinishReason = ConvertFinishReason(response.StopReason),
            PromptCache = cacheInfo
        };
    }

    private LLMFinishReason ConvertFinishReason(string? stopReason)
    {
        return stopReason switch
        {
            "end_turn" => LLMFinishReason.Stop,
            "max_tokens" => LLMFinishReason.MaxTokens,
            "tool_use" => LLMFinishReason.ToolCall,
            "stop_sequence" => LLMFinishReason.Stop,
            _ => LLMFinishReason.Stop
        };
    }

    private static string MapEphemeralTtl(TimeSpan ttl)
    {
        var five = TimeSpan.FromMinutes(5);
        var oneHour = TimeSpan.FromHours(1);
        var pickFive = Math.Abs((ttl - five).TotalMinutes);
        var pickHour = Math.Abs((ttl - oneHour).TotalMinutes);
        return pickFive <= pickHour ? "5m" : "1h";
    }

    private LLMPromptCacheInfo? ConvertCacheControl(AnthropicCacheControlResponse? cacheControl)
    {
        if (cacheControl?.Id == null)
        {
            return null;
        }

        DateTimeOffset? expiresAt = null;
        if (!string.IsNullOrEmpty(cacheControl.ExpiresAt) &&
            DateTimeOffset.TryParse(cacheControl.ExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            expiresAt = parsed.ToUniversalTime();
        }

        return new LLMPromptCacheInfo
        {
            CacheId = cacheControl.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt
        };
    }

    private IReadOnlyList<LLMModel> CreateModels()
    {
        return AnthropicModels.Capabilities.Select(kvp => new LLMModel
        {
            Id = kvp.Key,
            Name = GetModelDisplayName(kvp.Key),
            MaxTokens = kvp.Value.MaxTokens,
            ContextWindow = kvp.Value.MaxTokens,
            SupportsTools = kvp.Value.SupportsTools,
            SupportsStreaming = kvp.Value.SupportsStreaming,
            CostPerInputToken = GetModelInputCost(kvp.Key),
            CostPerOutputToken = GetModelOutputCost(kvp.Key)
        }).ToList();
    }

    private string GetModelDisplayName(string modelId)
    {
        return modelId switch
        {
            AnthropicModels.ClaudeOpus41 => "Claude Opus 4.1",
            AnthropicModels.ClaudeOpus4 => "Claude Opus 4",
            AnthropicModels.ClaudeSonnet4 => "Claude Sonnet 4",
            AnthropicModels.Claude37Sonnet => "Claude Sonnet 3.7",
            AnthropicModels.Claude35Haiku => "Claude Haiku 3.5",
            _ => modelId
        };
    }

    private decimal GetModelInputCost(string modelId)
    {
        return modelId switch
        {
            AnthropicModels.ClaudeOpus41 => 15.00m,
            AnthropicModels.ClaudeOpus4 => 15.00m,
            AnthropicModels.ClaudeSonnet4 => 3.00m,
            AnthropicModels.Claude37Sonnet => 3.00m,
            AnthropicModels.Claude35Haiku => 0.80m,
            _ => 0m
        };
    }

    private decimal GetModelOutputCost(string modelId)
    {
        return modelId switch
        {
            AnthropicModels.ClaudeOpus41 => 18.75m,
            AnthropicModels.ClaudeOpus4 => 18.75m,
            AnthropicModels.ClaudeSonnet4 => 3.75m,
            AnthropicModels.Claude37Sonnet => 3.75m,
            AnthropicModels.Claude35Haiku => 1.00m,
            _ => 0m
        };
    }
}
