using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;

namespace CodePunk.Core.Providers;

/// <summary>
/// OpenAI LLM provider implementation
/// </summary>
public class OpenAIProvider : ILLMProvider
{
    private readonly HttpClient _httpClient;
    private readonly LLMProviderConfig _config;
    private readonly JsonSerializerOptions _jsonOptions;

    public string Name => "OpenAI";

    public IReadOnlyList<LLMModel> Models { get; } = new[]
    {
        new LLMModel
        {
            Id = "gpt-4.1",
            Name = "GPT-4.1",
            Description = "Latest flagship GPT-4 generation model",
            MaxTokens = 4096,
            ContextWindow = 128000,
            CostPerInputToken = 0m,
            CostPerOutputToken = 0m,
            SupportsTools = true,
            SupportsStreaming = true
        },
        new LLMModel
        {
            Id = "gpt-4.1-mini",
            Name = "GPT-4.1 Mini",
            Description = "Smaller, efficient 4.1 family model",
            MaxTokens = 4096,
            ContextWindow = 128000,
            CostPerInputToken = 0m,
            CostPerOutputToken = 0m,
            SupportsTools = true,
            SupportsStreaming = true
        },
        new LLMModel
        {
            Id = "gpt-4o",
            Name = "GPT-4o",
            Description = "Multimodal GPT-4o model",
            MaxTokens = 4096,
            ContextWindow = 128000,
            CostPerInputToken = 0.005m / 1000,
            CostPerOutputToken = 0.015m / 1000,
            SupportsTools = true,
            SupportsStreaming = true
        },
        new LLMModel
        {
            Id = "gpt-4o-mini",
            Name = "GPT-4o Mini",
            Description = "Fast, low-cost GPT-4o variant",
            MaxTokens = 4096,
            ContextWindow = 128000,
            CostPerInputToken = 0.00015m / 1000,
            CostPerOutputToken = 0.0006m / 1000,
            SupportsTools = true,
            SupportsStreaming = true
        },
        new LLMModel
        {
            Id = "gpt-3.5-turbo",
            Name = "GPT-3.5 Turbo (Legacy)",
            Description = "Legacy model (kept for backwards compatibility)",
            MaxTokens = 4096,
            ContextWindow = 16385,
            CostPerInputToken = 0.0015m / 1000,
            CostPerOutputToken = 0.002m / 1000,
            SupportsTools = true,
            SupportsStreaming = true
        }
    };

    public OpenAIProvider(HttpClient httpClient, LLMProviderConfig config)
    {
        _httpClient = httpClient;
        _config = config;
        
        if (!string.IsNullOrEmpty(_config.BaseUrl))
        {
            _httpClient.BaseAddress = new Uri(_config.BaseUrl);
        }
        else
        {
            _httpClient.BaseAddress = new Uri("https://api.openai.com/v1/");
        }

        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_config.ApiKey}");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "CodePunk/1.0");

        foreach (var header in _config.ExtraHeaders)
        {
            _httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
        }

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <summary>
    /// Attempt to fetch available models from the OpenAI service. Falls back to an empty list on error.
    /// </summary>
    public async Task<IReadOnlyList<LLMModel>> FetchModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var resp = await _httpClient.GetFromJsonAsync<OpenAIModelList>("models", _jsonOptions, cancellationToken);
            if (resp?.Data == null) return Array.Empty<LLMModel>();

            var list = resp.Data.Select(d => new LLMModel
            {
                Id = d.Id,
                Name = d.Id,
                Description = d.Purpose ?? string.Empty,
                MaxTokens = 4096,
                ContextWindow = 4096,
                CostPerInputToken = 0m,
                CostPerOutputToken = 0m,
                SupportsTools = true,
                SupportsStreaming = true
            }).ToList();

            return list;
        }
        catch
        {
            return Array.Empty<LLMModel>();
        }
    }

    public async Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        var openAIRequest = ConvertToOpenAIRequest(request, stream: false);
        
        var response = await _httpClient.PostAsJsonAsync("chat/completions", openAIRequest, _jsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        var openAIResponse = await response.Content.ReadFromJsonAsync<OpenAIChatResponse>(_jsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Failed to deserialize OpenAI response");

        return ConvertFromOpenAIResponse(openAIResponse, request);
    }

    public async IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var openAIRequest = ConvertToOpenAIRequest(request, stream: true);
        
        var response = await _httpClient.PostAsJsonAsync("chat/completions", openAIRequest, _jsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: "))
                continue;

            var data = line[6..]; // Remove "data: " prefix
            if (data == "[DONE]")
            {
                yield return new LLMStreamChunk { IsComplete = true };
                break;
            }

            OpenAIChatStreamResponse? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<OpenAIChatStreamResponse>(data, _jsonOptions);
            }
            catch
            {
                continue; // Skip malformed chunks
            }

            if (chunk?.Choices?.FirstOrDefault() is { } choice)
            {
                yield return ConvertFromOpenAIStreamChunk(choice, request);
            }
        }
    }

    private object ConvertToOpenAIRequest(LLMRequest request, bool stream)
    {
        var messages = new List<object>();
        
        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            messages.Add(new { role = "system", content = request.SystemPrompt });
        }

        foreach (var message in request.Messages)
        {
            switch (message.Role)
            {
                case MessageRole.User:
                    messages.Add(new
                    {
                        role = "user",
                        content = string.Join("\n", message.Parts.OfType<TextPart>().Select(p => p.Content))
                    });
                    break;
                    
                case MessageRole.Assistant:
                    var assistantMessage = new Dictionary<string, object>
                    {
                        ["role"] = "assistant"
                    };

                    var textParts = message.Parts.OfType<TextPart>().ToList();
                    var toolCalls = message.Parts.OfType<ToolCallPart>().ToList();

                    if (textParts.Any())
                    {
                        assistantMessage["content"] = string.Join("\n", textParts.Select(p => p.Content));
                    }

                    if (toolCalls.Any())
                    {
                        assistantMessage["tool_calls"] = toolCalls.Select(tc => new
                        {
                            id = tc.Id,
                            type = "function",
                            function = new
                            {
                                name = tc.Name,
                                arguments = tc.Arguments.GetRawText()
                            }
                        }).ToArray();
                    }

                    messages.Add(assistantMessage);
                    break;
                    
                case MessageRole.Tool:
                    foreach (var toolResult in message.Parts.OfType<ToolResultPart>())
                    {
                        messages.Add(new
                        {
                            role = "tool",
                            tool_call_id = toolResult.ToolCallId,
                            content = toolResult.Content
                        });
                    }
                    break;
            }
        }

        var requestObj = new Dictionary<string, object>
        {
            ["model"] = request.ModelId,
            ["messages"] = messages,
            ["max_tokens"] = request.MaxTokens,
            ["temperature"] = request.Temperature,
            ["top_p"] = request.TopP,
            ["stream"] = stream
        };

        if (request.Tools?.Any() == true)
        {
            requestObj["tools"] = request.Tools.Select(tool => new
            {
                type = "function",
                function = new
                {
                    name = tool.Name,
                    description = tool.Description,
                    parameters = JsonSerializer.Deserialize<object>(tool.Parameters.GetRawText())
                }
            }).ToArray();
        }

        return requestObj;
    }

    private LLMResponse ConvertFromOpenAIResponse(OpenAIChatResponse response, LLMRequest request)
    {
        var choice = response.Choices?.FirstOrDefault()
            ?? throw new InvalidOperationException("OpenAI response contains no choices");

        var content = choice.Message?.Content ?? string.Empty;
        var toolCalls = choice.Message?.ToolCalls?.Select(tc => new ToolCall
        {
            Id = tc.Id,
            Name = tc.Function.Name,
            Arguments = JsonSerializer.SerializeToElement(tc.Function.Arguments)
        }).ToList();

        var usage = response.Usage != null ? new LLMUsage
        {
            InputTokens = response.Usage.PromptTokens,
            OutputTokens = response.Usage.CompletionTokens,
            EstimatedCost = CalculateCost(response.Usage.PromptTokens, response.Usage.CompletionTokens, request.ModelId)
        } : null;

        var finishReason = choice.FinishReason switch
        {
            "stop" => LLMFinishReason.Stop,
            "length" => LLMFinishReason.MaxTokens,
            "tool_calls" => LLMFinishReason.ToolCall,
            "content_filter" => LLMFinishReason.ContentFilter,
            _ => LLMFinishReason.Stop
        };

        return new LLMResponse
        {
            Content = content,
            ToolCalls = toolCalls,
            Usage = usage,
            FinishReason = finishReason
        };
    }

    private LLMStreamChunk ConvertFromOpenAIStreamChunk(OpenAIChatChoice choice, LLMRequest request)
    {
        var delta = choice.Delta;
        
        ToolCall? toolCall = null;
        if (delta?.ToolCalls?.FirstOrDefault() is { } tc)
        {
            toolCall = new ToolCall
            {
                Id = tc.Id,
                Name = tc.Function?.Name ?? "",
                Arguments = JsonSerializer.SerializeToElement(tc.Function?.Arguments ?? "{}")
            };
        }

        var finishReason = choice.FinishReason switch
        {
            "stop" => LLMFinishReason.Stop,
            "length" => LLMFinishReason.MaxTokens,
            "tool_calls" => LLMFinishReason.ToolCall,
            "content_filter" => LLMFinishReason.ContentFilter,
            null => (LLMFinishReason?)null,
            _ => LLMFinishReason.Stop
        };

        return new LLMStreamChunk
        {
            Content = delta?.Content,
            ToolCall = toolCall,
            FinishReason = finishReason,
            IsComplete = choice.FinishReason != null
        };
    }

    private decimal CalculateCost(int inputTokens, int outputTokens, string modelId)
    {
        var model = Models.FirstOrDefault(m => m.Id == modelId);
        if (model == null) return 0m;

        return (inputTokens * model.CostPerInputToken) + (outputTokens * model.CostPerOutputToken);
    }

    private record OpenAIChatResponse
    {
        public string? Id { get; init; }
        public OpenAIChatChoice[]? Choices { get; init; }
        public OpenAIUsage? Usage { get; init; }
    }

    private record OpenAIChatStreamResponse
    {
        public string? Id { get; init; }
        public OpenAIChatChoice[]? Choices { get; init; }
    }

    private record OpenAIModelList
    {
        public OpenAIModel[]? Data { get; init; }
    }

    private record OpenAIModel
    {
        public string Id { get; init; } = string.Empty;
        public string? Purpose { get; init; }
    }

    private record OpenAIChatChoice
    {
        public int Index { get; init; }
        public OpenAIChatMessage? Message { get; init; }
        public OpenAIChatDelta? Delta { get; init; }
        public string? FinishReason { get; init; }
    }

    private record OpenAIChatMessage
    {
        public string? Role { get; init; }
        public string? Content { get; init; }
        public OpenAIToolCall[]? ToolCalls { get; init; }
    }

    private record OpenAIChatDelta
    {
        public string? Role { get; init; }
        public string? Content { get; init; }
        public OpenAIToolCall[]? ToolCalls { get; init; }
    }

    private record OpenAIToolCall
    {
        public string Id { get; init; } = "";
        public string Type { get; init; } = "";
        public OpenAIFunction Function { get; init; } = new();
    }

    private record OpenAIFunction
    {
        public string Name { get; init; } = "";
        public string Arguments { get; init; } = "";
    }

    private record OpenAIUsage
    {
        public int PromptTokens { get; init; }
        public int CompletionTokens { get; init; }
        public int TotalTokens { get; init; }
    }
}
