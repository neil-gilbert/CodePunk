using System.Net.Http.Json;
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
        _httpClient.BaseAddress = new Uri(config.BaseUrl);
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
            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

            _logger.LogDebug("Sending request to Anthropic API for model {Model}", request.ModelId);

            var response = await _httpClient.PostAsJsonAsync("/messages", anthropicRequest, jsonOptions, cancellationToken);
            response.EnsureSuccessStatusCode();

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
        anthropicRequest.Stream = true;

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var content = JsonContent.Create(anthropicRequest, options: jsonOptions);

        _logger.LogDebug("Starting streaming request to Anthropic API for model {Model}", request.ModelId);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/messages")
        {
            Content = content
        };

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var tokenUsage = new LLMUsage();

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: "))
                continue;

            var jsonData = line.Substring(6); // Remove "data: " prefix
            
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

            if (streamResponse?.Delta?.Text != null)
            {
                yield return new LLMStreamChunk
                {
                    Content = streamResponse.Delta.Text,
                    IsComplete = streamResponse.Type == "message_stop"
                };
            }

            if (streamResponse?.Usage != null)
            {
                tokenUsage = new LLMUsage
                {
                    InputTokens = streamResponse.Usage.InputTokens,
                    OutputTokens = streamResponse.Usage.OutputTokens
                };
            }

            if (streamResponse?.Type == "message_stop")
            {
                yield return new LLMStreamChunk
                {
                    Usage = tokenUsage,
                    IsComplete = true,
                    FinishReason = LLMFinishReason.Stop
                };
                break;
            }
        }
    }

    private AnthropicRequest ConvertToAnthropicRequest(LLMRequest request)
    {
        var (messages, systemPrompt) = ConvertMessages(request.Messages, request.SystemPrompt);

        return new AnthropicRequest
        {
            Model = request.ModelId,
            MaxTokens = request.MaxTokens,
            Temperature = request.Temperature,
            System = systemPrompt,
            Messages = messages
        };
    }

    private (List<AnthropicMessage>, string?) ConvertMessages(IReadOnlyList<Message> messages, string? systemPrompt)
    {
        var anthropicMessages = new List<AnthropicMessage>();
        string? extractedSystemPrompt = systemPrompt;

        foreach (var message in messages)
        {
            // Extract system messages and combine them
            if (message.Role == MessageRole.System)
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

            // Skip tool messages for now (can be implemented later)
            if (message.Role == MessageRole.Tool)
                continue;

            var content = GetMessageContent(message);
            if (!string.IsNullOrEmpty(content))
            {
                anthropicMessages.Add(new AnthropicMessage
                {
                    Role = message.Role == MessageRole.User ? "user" : "assistant",
                    Content = content
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

        return new LLMResponse
        {
            Content = content,
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
