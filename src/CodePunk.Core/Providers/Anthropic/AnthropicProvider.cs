using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using Microsoft.Extensions.Logging;

namespace CodePunk.Core.Providers.Anthropic;

public class AnthropicProvider : ILLMProvider
{
    private readonly HttpClient _httpClient;
    private readonly AnthropicConfiguration _config;
    private readonly ILogger<AnthropicProvider> _logger;

    public string Name => "Anthropic";

    public IReadOnlyList<LLMModel> Models { get; }

    public AnthropicProvider(HttpClient httpClient, AnthropicConfiguration config, ILogger<AnthropicProvider> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;

        // Configure HTTP client
        var baseUrl = config.BaseUrl.TrimEnd('/') + "/";
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.DefaultRequestHeaders.Add("x-api-key", config.ApiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", config.Version);
        _httpClient.Timeout = config.Timeout;

        Models = CreateModels();
    }

    public async Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
        var anthropicRequest = ConvertToAnthropicRequest(request);
        
        // Log request information to verify tools are being sent
        _logger.LogInformation("Anthropic request: Tools count={ToolCount}", anthropicRequest.Tools?.Count ?? 0);
        if (anthropicRequest.Tools?.Any() == true)
        {
            _logger.LogInformation("Anthropic request tools: {Tools}", 
                string.Join(", ", anthropicRequest.Tools.Select(t => t.Name)));
        }
        
        var jsonOptions = new JsonSerializerOptions {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        };
            _logger.LogDebug("Sending request to Anthropic API for model {Model}", request.ModelId);

            var response = await _httpClient.PostAsJsonAsync("messages", anthropicRequest, jsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Anthropic API error {Status}: {Body}", (int)response.StatusCode, errorBody);
                response.EnsureSuccessStatusCode();
            }

            var anthropicResponse = await response.Content.ReadFromJsonAsync<AnthropicResponse>(jsonOptions, cancellationToken);
            
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

    public async IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var anthropicRequest = ConvertToAnthropicRequest(request);
        
        // Log request information to verify tools are being sent
        _logger.LogInformation("Anthropic streaming request: Tools count={ToolCount}", anthropicRequest.Tools?.Count ?? 0);
        if (anthropicRequest.Tools?.Any() == true)
        {
            _logger.LogInformation("Anthropic streaming request tools: {Tools}", 
                string.Join(", ", anthropicRequest.Tools.Select(t => t.Name)));
        }
        
        anthropicRequest.Stream = true;

        var jsonOptions = new JsonSerializerOptions {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        };
        var content = JsonContent.Create(anthropicRequest, options: jsonOptions);

        _logger.LogDebug("Starting streaming request to Anthropic API for model {Model}", request.ModelId);

    using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "messages")
        {
            Content = content
        };

    // Ensure server-sent events are returned for streaming
    httpRequest.Headers.Accept.Clear();
    httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Anthropic streaming API error {Status}: {Body}", (int)response.StatusCode, err);
            response.EnsureSuccessStatusCode();
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        _logger.LogInformation("Starting to read Anthropic streaming response");
        
        LLMUsage? tokenUsage = null;
        var toolCallBuilders = new Dictionary<int, (ToolCall toolCall, StringBuilder jsonBuilder)>();

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            
            if (string.IsNullOrEmpty(line))
                continue;

            var trimmed = line.Trim();
            // Remove UTF-8 BOM if present
            if (trimmed.Length > 0 && trimmed[0] == '\uFEFF')
            {
                trimmed = trimmed.TrimStart('\uFEFF');
            }
            if (trimmed.StartsWith("event: "))
            {
                // We can ignore event lines for now, but might be useful for more robust parsing
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
                    streamResponse = JsonSerializer.Deserialize<AnthropicStreamResponse>(jsonData, jsonOptions);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse streaming response: {JsonData}", jsonData);
                    continue;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error parsing streaming response: {JsonData}", jsonData);
                    continue;
                }

                if (streamResponse == null) continue;

                switch (streamResponse.Type)
                {
                    case "message_start":
                        if (streamResponse.Message?.Usage != null)
                        {
                            tokenUsage = new LLMUsage { InputTokens = streamResponse.Message.Usage.InputTokens };
                        }
                        break;

                    case "content_block_start":
                        if (streamResponse.ContentBlock?.Type == "tool_use" && streamResponse.Index.HasValue)
                        {
                            var toolCall = new ToolCall
                            {
                                Id = streamResponse.ContentBlock.Id ?? string.Empty,
                                Name = streamResponse.ContentBlock.Name ?? string.Empty,
                            };
                            toolCallBuilders[streamResponse.Index.Value] = (toolCall, new StringBuilder());
                            _logger.LogInformation("Started tool call stream for '{ToolName}' (ID: {ToolId}) at index {Index}", 
                                toolCall.Name, toolCall.Id, streamResponse.Index.Value);
                        }
                        break;

                    case "content_block_delta":
                        // Handle tool input streaming
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
                        // Handle text streaming
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
                            var (toolCall, jsonBuilder) = finishedBuilder;
                            var finalJson = jsonBuilder.ToString();
                            LLMStreamChunk? toolChunk = null;
                            try
                            {
                                var arguments = JsonSerializer.Deserialize<JsonElement>(finalJson);
                                _logger.LogInformation("Completed tool call stream for '{ToolName}' (ID: {ToolId}). Arguments: {Arguments}",
                                    toolCall.Name, toolCall.Id, finalJson);

                                toolChunk = new LLMStreamChunk
                                {
                                    ToolCall = new ToolCall
                                    {
                                        Id = toolCall.Id,
                                        Name = toolCall.Name,
                                        Arguments = arguments
                                    },
                                    IsComplete = false
                                };
                            }
                            catch (JsonException ex)
                            {
                                _logger.LogError(ex, "Failed to parse final JSON for tool call '{ToolName}' (ID: {ToolId}). JSON: {Json}",
                                    toolCall.Name, toolCall.Id, finalJson);
                            }

                            if (toolChunk != null)
                            {
                                yield return toolChunk;
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
                            
                            _logger.LogInformation("Anthropic streaming response completed with reason: {StopReason}", stopReason.GetString());
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
                        _logger.LogInformation("Anthropic streaming response stopped.");
                        // Prefer usage from this event; fall back to any earlier captured input tokens
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

    private AnthropicRequest ConvertToAnthropicRequest(LLMRequest request)
    {
        var (messages, systemPrompt) = ConvertMessages(request.Messages, request.SystemPrompt);
        var tools = ConvertTools(request.Tools);

        return new AnthropicRequest
        {
            Model = request.ModelId,
            MaxTokens = request.MaxTokens,
            Temperature = request.Temperature,
            System = systemPrompt,
            Messages = messages,
            Tools = tools?.Count > 0 ? tools : null
        };
    }

    private List<AnthropicTool>? ConvertTools(IReadOnlyList<LLMTool>? tools)
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
        
        return anthropicTools;
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
                MessageRole.Tool => "user", // Tool results are sent with the 'user' role
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
            
            // Text parts
            var textContent = string.Join("\n", message.Parts.OfType<TextPart>().Select(p => p.Content));
            if (!string.IsNullOrEmpty(textContent))
            {
                contentParts.Add(new AnthropicTextContent { Text = textContent });
            }

            // Tool call parts (from assistant)
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
            
            // Tool result parts (from user)
            var toolResultParts = message.Parts.OfType<ToolResultPart>().ToList();
            if (toolResultParts.Any())
            {
                role = "user"; // Tool results must be in a user message
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
                // If the message was originally an assistant message but we are adding tool calls,
                // ensure the role is correct.
                if (message.Role == MessageRole.Assistant)
                {
                    role = "assistant";
                }

                // Always send content as an array of content blocks per Anthropic Messages API
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
        
        // Extract tool calls from response
        var toolCalls = response.Content
            .Where(c => c.Type == "tool_use")
            .Select(c => new ToolCall
            {
                Id = c.Id ?? string.Empty,
                Name = c.Name ?? string.Empty,
                Arguments = JsonSerializer.SerializeToElement(c.Input ?? new object())
            })
            .ToList();

        // Log tool call information
        _logger.LogInformation("Anthropic response: Content blocks={ContentBlocks}, Tool calls found={ToolCallCount}", 
            response.Content.Count, toolCalls.Count);
        
        if (toolCalls.Any())
        {
            _logger.LogInformation("Found tool calls: {ToolNames}", string.Join(", ", toolCalls.Select(t => t.Name)));
        }

        return new LLMResponse
        {
            Content = content,
            ToolCalls = toolCalls.Any() ? toolCalls : null,
            Usage = response.Usage != null ? new LLMUsage
            {
                InputTokens = response.Usage.InputTokens,
                OutputTokens = response.Usage.OutputTokens
            } : null,
            FinishReason = ConvertFinishReason(response.StopReason)
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
            AnthropicModels.Claude35Sonnet => "Claude 3.5 Sonnet",
            AnthropicModels.Claude35Haiku => "Claude 3.5 Haiku",
            AnthropicModels.Claude3Opus => "Claude 3 Opus",
            AnthropicModels.Claude3Sonnet => "Claude 3 Sonnet",
            AnthropicModels.Claude3Haiku => "Claude 3 Haiku",
            _ => modelId
        };
    }

    private decimal GetModelInputCost(string modelId)
    {
        // Costs per 1M tokens as of August 2024
        return modelId switch
        {
            AnthropicModels.Claude35Sonnet => 3.00m,
            AnthropicModels.Claude35Haiku => 0.25m,
            AnthropicModels.Claude3Opus => 15.00m,
            AnthropicModels.Claude3Sonnet => 3.00m,
            AnthropicModels.Claude3Haiku => 0.25m,
            _ => 0m
        };
    }

    private decimal GetModelOutputCost(string modelId)
    {
        // Costs per 1M tokens as of August 2024
        return modelId switch
        {
            AnthropicModels.Claude35Sonnet => 15.00m,
            AnthropicModels.Claude35Haiku => 1.25m,
            AnthropicModels.Claude3Opus => 75.00m,
            AnthropicModels.Claude3Sonnet => 15.00m,
            AnthropicModels.Claude3Haiku => 1.25m,
            _ => 0m
        };
    }
}
