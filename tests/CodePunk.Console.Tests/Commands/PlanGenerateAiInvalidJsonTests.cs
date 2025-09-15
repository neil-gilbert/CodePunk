using System.Text.Json;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Services;
using CodePunk.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FluentAssertions;

namespace CodePunk.Console.Tests.Commands;

public class PlanGenerateAiInvalidJsonTests : ConsoleTestBase
{
    private class BadProvider : ILLMProvider
    {
        public string Name => "Bad";
        public IReadOnlyList<LLMModel> Models { get; } = new [] { new LLMModel { Id = "bad-model", Name = "Bad Model" } };
        public Task<IReadOnlyList<LLMModel>> FetchModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Models);
        public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default)
        {
            var content = "NOT JSON";
            return Task.FromResult(new LLMResponse { Content = content });
        }
        public async IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        { yield break; }
    }

    private class BadLLMService : ILLMService
    {
        private readonly BadProvider _provider = new();
        public IReadOnlyList<ILLMProvider> GetProviders() => new [] { _provider };
        public ILLMProvider? GetProvider(string name) => name == _provider.Name ? _provider : null;
        public ILLMProvider GetDefaultProvider() => _provider;
        public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken ct = default) => _provider.SendAsync(request, ct);
        public Task<LLMResponse> SendAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default) => _provider.SendAsync(request, cancellationToken);
        public IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, CancellationToken ct = default) => _provider.StreamAsync(request, ct);
        public IAsyncEnumerable<LLMStreamChunk> StreamAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default) => _provider.StreamAsync(request, cancellationToken);
        public Task<Message> SendMessageAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default) => Task.FromResult(Message.Create(conversationHistory.Last().SessionId, MessageRole.Assistant, new []{ new TextPart("bad") }));
    public IAsyncEnumerable<LLMStreamChunk> SendMessageStreamAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default) => StreamAsync(new LLMRequest{ ModelId = _provider.Models.First().Id, Messages = conversationHistory.ToList().AsReadOnly() });
    }

    [Fact]
    public void PlanGenerateAi_InvalidJson_EmitsError()
    {
        WithServices(s => s.AddScoped<ILLMService, BadLLMService>());
        var output = Run("plan generate --ai --goal \"Bad output test\" --provider Bad --model bad-model --json");
        var obj = JsonLast(output);
        obj.GetProperty("schema").GetString().Should().Be("plan.generate.ai.v1");
        obj.TryGetProperty("error", out var err).Should().BeTrue();
        err.GetProperty("code").GetString().Should().Be("ModelOutputInvalid");
    }
}
