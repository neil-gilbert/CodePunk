using System.Net;
using System.Net.Http;
using System.Text;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using CodePunk.Core.Providers.Anthropic;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodePunk.Core.Tests.Providers.Anthropic;

/// <summary>
/// Verifies streaming handling for server_tool_use/mcp_tool_use events.
/// </summary>
public class AnthropicProviderSseTests
{
    [Fact]
    public async Task StreamAsync_EmitsContent_ForServerToolUse()
    {
        var sse = new StringBuilder();
        sse.AppendLine("event: content_block_start");
        sse.AppendLine("data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"server_tool_use\",\"id\":\"id1\",\"name\":\"web_search\"}}\n");
        sse.AppendLine("event: content_block_delta");
        sse.AppendLine("data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"q\\\":\\\"hi\\\"\"}}\n");
        sse.AppendLine("event: content_block_stop");
        sse.AppendLine("data: {\"type\":\"content_block_stop\",\"index\":0}\n");
        sse.AppendLine("event: message_stop");
        sse.AppendLine("data: {\"type\":\"message_stop\",\"usage\":{\"input_tokens\":10,\"output_tokens\":5}}\n");

        var handler = new SseHandler(sse.ToString());
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/v1/") };
        var config = new AnthropicConfiguration { ApiKey = "key", BaseUrl = "https://api.anthropic.com/v1", Version = "2023-06-01" };
        var provider = new AnthropicProvider(http, config, new CodePunk.Core.Caching.PromptCacheOptions(), NullLogger<AnthropicProvider>.Instance);

        var request = new LLMRequest
        {
            ModelId = "claude-3-5-sonnet",
            Messages = new[] { Message.Create("s", MessageRole.User, new[] { new TextPart("hi") }) }
        };

        var chunks = new List<LLMStreamChunk>();
        await foreach (var c in provider.StreamAsync(request))
        {
            chunks.Add(c);
        }

        chunks.Should().NotBeEmpty();
        var server = chunks.FirstOrDefault(c => c.ServerToolUse != null);
        server.Should().NotBeNull();
        server!.ServerToolUse!.Query.Should().Be("hi");
        chunks.Last().IsComplete.Should().BeTrue();
    }

    private sealed class SseHandler : HttpMessageHandler
    {
        private readonly string _sse;
        public SseHandler(string sse) { _sse = sse; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var isStream = request.Headers.Accept.Any(a => a.MediaType == "text/event-stream");
            if (!isStream && request.RequestUri!.AbsolutePath.EndsWith("/messages"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"id\":\"m\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]}", Encoding.UTF8, "application/json")
                });
            }

            var resp = new HttpResponseMessage(HttpStatusCode.OK);
            var bytes = Encoding.UTF8.GetBytes(_sse);
            resp.Content = new ByteArrayContent(bytes);
            resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(resp);
        }
    }
}
