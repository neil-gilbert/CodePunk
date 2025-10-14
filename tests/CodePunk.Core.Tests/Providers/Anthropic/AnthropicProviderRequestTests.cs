using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using CodePunk.Core.Providers.Anthropic;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodePunk.Core.Tests.Providers.Anthropic;

/// <summary>
/// Verifies request construction (TTL mapping) and retry behavior.
/// </summary>
public class AnthropicProviderRequestTests
{
    [Theory]
    [InlineData(5, "5m")]
    [InlineData(60, "1h")]
    public async Task SendAsync_IncludesEphemeralTtl_OnSystemAndLastTool(int ttlMinutes, string expected)
    {
        var handler = new CaptureHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/v1/") };
        var config = new AnthropicConfiguration { ApiKey = "key", BaseUrl = "https://api.anthropic.com/v1", Version = "2023-06-01" };
        var opts = new CodePunk.Core.Caching.PromptCacheOptions { DefaultTtl = TimeSpan.FromMinutes(ttlMinutes) };
        var provider = new AnthropicProvider(http, config, opts, NullLogger<AnthropicProvider>.Instance);

        var request = new LLMRequest
        {
            ModelId = "claude-3-5-sonnet",
            SystemPrompt = "system",
            UseEphemeralCache = true,
            Messages = new[] { Message.Create("s", MessageRole.User, new[] { new TextPart("hi") }) },
            Tools = new[]
            {
                new LLMTool { Name = "t1", Description = "d", Parameters = JsonDocument.Parse("{}").RootElement }
            }
        };

        handler.ResponseJson = "{\"id\":\"m\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"ok\"}],\"usage\":{\"input_tokens\":10,\"output_tokens\":5}}";

        await provider.SendAsync(request, CancellationToken.None);

        handler.LastRequestBody.Should().NotBeNullOrEmpty();
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var root = doc.RootElement;
        var hasSystemTtl = root.GetProperty("system")[0].GetProperty("cache_control").GetProperty("ttl").GetString();
        hasSystemTtl.Should().Be(expected);
        var tools = root.GetProperty("tools");
        tools.GetArrayLength().Should().Be(1);
        var lastTool = tools[0].GetProperty("cache_control").GetProperty("ttl").GetString();
        lastTool.Should().Be(expected);
    }

    [Fact]
    public async Task SendAsync_RetriesOn429_WithRetryAfter()
    {
        var handler = new FlakyHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/v1/") };
        var config = new AnthropicConfiguration { ApiKey = "key", BaseUrl = "https://api.anthropic.com/v1", Version = "2023-06-01" };
        var provider = new AnthropicProvider(http, config, new CodePunk.Core.Caching.PromptCacheOptions(), NullLogger<AnthropicProvider>.Instance);

        var request = new LLMRequest
        {
            ModelId = "claude-3-5-sonnet",
            Messages = new[] { Message.Create("s", MessageRole.User, new[] { new TextPart("hi") }) }
        };

        var resp = await provider.SendAsync(request, CancellationToken.None);
        resp.Content.Should().Be("ok");
        handler.Calls.Should().Be(2);
    }

    /// <summary>
    /// Captures posted body and returns a 200 OK with a simple message response.
    /// </summary>
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }
        public string ResponseJson { get; set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content != null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ResponseJson, Encoding.UTF8, "application/json")
            };
        }
    }

    /// <summary>
    /// Returns 429 once with Retry-After, then 200 OK.
    /// </summary>
    private sealed class FlakyHandler : HttpMessageHandler
    {
        private int _calls;
        public int Calls => _calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _calls++;
            if (_calls == 1)
            {
                var tooMany = new HttpResponseMessage((HttpStatusCode)429);
                tooMany.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
                tooMany.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                return Task.FromResult(tooMany);
            }
            var ok = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"m\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]}", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(ok);
        }
    }
}
