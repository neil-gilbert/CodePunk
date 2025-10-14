using System.Net;
using System.Net.Http;
using System.Text;
using CodePunk.Core.Providers.Anthropic;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;

namespace CodePunk.Core.Tests.Providers.Anthropic;

/// <summary>
/// Verifies shaped error messages for common HTTP statuses.
/// </summary>
public class AnthropicProviderErrorTests
{
    [Fact]
    public async Task SendAsync_Returns401_UnauthorizedMessage()
    {
        var handler = new StaticHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/v1/") };
        var provider = new AnthropicProvider(http, new AnthropicConfiguration { ApiKey = "x" }, new CodePunk.Core.Caching.PromptCacheOptions(), NullLogger<AnthropicProvider>.Instance);

        var request = new LLMRequest { ModelId = "m", Messages = new[] { Message.Create("s", MessageRole.User, new[] { new TextPart("hi") }) } };

        var act = () => provider.SendAsync(request);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Anthropic unauthorized. Check API key.");
    }

    [Fact]
    public async Task SendAsync_Returns500_ServerErrorMessage()
    {
        var handler = new StaticHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("server boom", Encoding.UTF8, "text/plain")
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/v1/") };
        var provider = new AnthropicProvider(http, new AnthropicConfiguration { ApiKey = "x" }, new CodePunk.Core.Caching.PromptCacheOptions(), NullLogger<AnthropicProvider>.Instance);

        var request = new LLMRequest { ModelId = "m", Messages = new[] { Message.Create("s", MessageRole.User, new[] { new TextPart("hi") }) } };

        var act = () => provider.SendAsync(request);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Anthropic server error. Please retry.");
    }

    private sealed class StaticHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public StaticHandler(HttpResponseMessage response) { _response = response; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(_response);
    }
}
