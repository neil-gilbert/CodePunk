using System.Net;
using System.Text.Json;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using CodePunk.Core.Providers.Anthropic;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace CodePunk.Core.Tests.Providers.Anthropic;

public class AnthropicProviderTests
{
    private readonly Mock<ILogger<AnthropicProvider>> _mockLogger;
    private readonly AnthropicConfiguration _config;

    public AnthropicProviderTests()
    {
        _mockLogger = new Mock<ILogger<AnthropicProvider>>();
        _config = new AnthropicConfiguration
        {
            ApiKey = "test-api-key",
            BaseUrl = "https://api.anthropic.com/v1",
            DefaultModel = AnthropicModels.Claude35Sonnet,
            MaxTokens = 4096,
            Temperature = 0.7,
            Version = "2023-06-01"
        };
    }

    [Fact]
    public void Name_ShouldReturnAnthropic()
    {
        // Arrange
        var httpClient = new HttpClient();
        var provider = new AnthropicProvider(httpClient, _config, _mockLogger.Object);

        // Act & Assert
        provider.Name.Should().Be("Anthropic");
    }

    [Fact]
    public void Models_ShouldReturnConfiguredModels()
    {
        // Arrange
        var httpClient = new HttpClient();
        var provider = new AnthropicProvider(httpClient, _config, _mockLogger.Object);

        // Act
        var models = provider.Models;

        // Assert
        models.Should().NotBeEmpty();
        models.Should().Contain(m => m.Id == AnthropicModels.Claude35Sonnet);
        models.Should().Contain(m => m.Id == AnthropicModels.Claude35Haiku);
        models.Should().Contain(m => m.Id == AnthropicModels.Claude3Opus);
        
        var claude35Sonnet = models.First(m => m.Id == AnthropicModels.Claude35Sonnet);
        claude35Sonnet.Name.Should().Be("Claude 3.5 Sonnet");
        claude35Sonnet.MaxTokens.Should().Be(200000);
        claude35Sonnet.SupportsStreaming.Should().BeTrue();
        claude35Sonnet.SupportsTools.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_WithValidRequest_ShouldReturnResponse()
    {
        // Arrange
        var mockResponse = new AnthropicResponse
        {
            Id = "msg_test",
            Type = "message",
            Role = "assistant",
            Content = new List<AnthropicContent>
            {
                new() { Type = "text", Text = "Hello, world!" }
            },
            Model = AnthropicModels.Claude35Sonnet,
            StopReason = "end_turn",
            Usage = new AnthropicUsage
            {
                InputTokens = 10,
                OutputTokens = 5
            }
        };

        var httpClient = CreateMockHttpClient(mockResponse);
        var provider = new AnthropicProvider(httpClient, _config, _mockLogger.Object);

        var request = new LLMRequest
        {
            ModelId = AnthropicModels.Claude35Sonnet,
            Messages = new[]
            {
                Message.Create("session-1", MessageRole.User, new[] { new TextPart("Hello") })
            },
            MaxTokens = 100,
            Temperature = 0.5
        };

        // Act
        var response = await provider.SendAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.Content.Should().Be("Hello, world!");
        response.Usage.Should().NotBeNull();
        response.Usage!.InputTokens.Should().Be(10);
        response.Usage.OutputTokens.Should().Be(5);
        response.FinishReason.Should().Be(LLMFinishReason.Stop);
    }

    [Fact]
    public async Task SendAsync_WithHttpError_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var httpClient = CreateMockHttpClientWithError(HttpStatusCode.Unauthorized);
        var provider = new AnthropicProvider(httpClient, _config, _mockLogger.Object);

        var request = new LLMRequest
        {
            ModelId = AnthropicModels.Claude35Sonnet,
            Messages = new[]
            {
                Message.Create("session-1", MessageRole.User, new[] { new TextPart("Hello") })
            }
        };

        // Act & Assert
        await provider.Invoking(p => p.SendAsync(request))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Failed to call Anthropic API");
    }

    [Fact]
    public async Task StreamAsync_WithValidRequest_ShouldReturnStreamChunks()
    {
        // Arrange
        var streamData = new[]
        {
            "data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"Hello\"}}",
            "data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\" world\"}}",
            "data: {\"type\":\"message_stop\",\"usage\":{\"input_tokens\":10,\"output_tokens\":5}}"
        };

        var httpClient = CreateMockHttpClientForStreaming(streamData);
        var provider = new AnthropicProvider(httpClient, _config, _mockLogger.Object);

        var request = new LLMRequest
        {
            ModelId = AnthropicModels.Claude35Sonnet,
            Messages = new[]
            {
                Message.Create("session-1", MessageRole.User, new[] { new TextPart("Hello") })
            }
        };

        // Act
        var chunks = new List<LLMStreamChunk>();
        await foreach (var chunk in provider.StreamAsync(request))
        {
            chunks.Add(chunk);
        }

        // Assert
        chunks.Should().HaveCount(3);
        chunks[0].Content.Should().Be("Hello");
        chunks[1].Content.Should().Be(" world");
        chunks[2].IsComplete.Should().BeTrue();
        chunks[2].Usage.Should().NotBeNull();
    }

    [Theory]
    [InlineData("end_turn", LLMFinishReason.Stop)]
    [InlineData("max_tokens", LLMFinishReason.MaxTokens)]
    [InlineData("tool_use", LLMFinishReason.ToolCall)]
    [InlineData("stop_sequence", LLMFinishReason.Stop)]
    [InlineData("unknown", LLMFinishReason.Stop)]
    public async Task SendAsync_WithDifferentStopReasons_ShouldMapCorrectly(string anthropicReason, LLMFinishReason expectedReason)
    {
        // Arrange
        var mockResponse = new AnthropicResponse
        {
            Id = "msg_test",
            Type = "message",
            Role = "assistant",
            Content = new List<AnthropicContent>
            {
                new() { Type = "text", Text = "Test response" }
            },
            Model = AnthropicModels.Claude35Sonnet,
            StopReason = anthropicReason
        };

        var httpClient = CreateMockHttpClient(mockResponse);
        var provider = new AnthropicProvider(httpClient, _config, _mockLogger.Object);

        var request = new LLMRequest
        {
            ModelId = AnthropicModels.Claude35Sonnet,
            Messages = new[]
            {
                Message.Create("session-1", MessageRole.User, new[] { new TextPart("Test") })
            }
        };

        // Act
        var response = await provider.SendAsync(request);

        // Assert
        response.FinishReason.Should().Be(expectedReason);
    }

    [Fact]
    public async Task SendAsync_WithSystemMessage_ShouldExtractSystemPrompt()
    {
        // Arrange
        var capturedRequest = (AnthropicRequest?)null;
        var httpClient = CreateMockHttpClientWithRequestCapture(new AnthropicResponse
        {
            Id = "msg_test",
            Content = new List<AnthropicContent> { new() { Type = "text", Text = "Response" } }
        }, request => capturedRequest = request);

        var provider = new AnthropicProvider(httpClient, _config, _mockLogger.Object);

        var request = new LLMRequest
        {
            ModelId = AnthropicModels.Claude35Sonnet,
            Messages = new[]
            {
                Message.Create("session-1", MessageRole.System, new[] { new TextPart("You are a helpful assistant") }),
                Message.Create("session-1", MessageRole.User, new[] { new TextPart("Hello") })
            },
            SystemPrompt = "Additional system context"
        };

        // Act
        await provider.SendAsync(request);

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.System.Should().Contain("Additional system context");
        capturedRequest.System.Should().Contain("You are a helpful assistant");
        capturedRequest.Messages.Should().HaveCount(1); // Only user message
        capturedRequest.Messages[0].Role.Should().Be("user");
    }

    private HttpClient CreateMockHttpClient(AnthropicResponse response)
    {
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var responseJson = JsonSerializer.Serialize(response, jsonOptions);
        
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            });

        return new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri(_config.BaseUrl)
        };
    }

    private HttpClient CreateMockHttpClientWithError(HttpStatusCode statusCode)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent("Unauthorized")
            });

        return new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri(_config.BaseUrl)
        };
    }

    private HttpClient CreateMockHttpClientForStreaming(string[] streamData)
    {
        // Simulate proper SSE framing with event: ... and data: ... lines separated by blank lines
        var sb = new System.Text.StringBuilder();
        foreach (var line in streamData)
        {
            // Infer event type from the JSON 'type' field for test clarity
            string eventName = "message";
            if (line.Contains("\"type\":\"content_block_delta\"")) eventName = "content_block_delta";
            else if (line.Contains("\"type\":\"message_stop\"")) eventName = "message_stop";

            sb.Append("event: ").Append(eventName).Append('\n');
            sb.Append("data: ").Append(line.Substring(line.IndexOf('{'))).Append('\n');
            sb.Append('\n');
        }
        var streamContent = sb.ToString();
        
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(streamContent);
                var ms = new System.IO.MemoryStream(bytes);
                var streamHttpContent = new StreamContent(ms);
                streamHttpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
                streamHttpContent.Headers.ContentLength = bytes.Length;
                var resp = new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = streamHttpContent,
                };
                return resp;
            });

        return new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri(_config.BaseUrl)
        };
    }

    private HttpClient CreateMockHttpClientWithRequestCapture(AnthropicResponse response, Action<AnthropicRequest> requestCapture)
    {
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var responseJson = JsonSerializer.Serialize(response, jsonOptions);
        
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (request, _) =>
            {
                if (request.Content != null)
                {
                    var requestJson = await request.Content.ReadAsStringAsync();
                    var anthropicRequest = JsonSerializer.Deserialize<AnthropicRequest>(requestJson, jsonOptions);
                    if (anthropicRequest != null)
                    {
                        requestCapture(anthropicRequest);
                    }
                }

                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
                };
            });

        return new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri(_config.BaseUrl)
        };
    }
}
