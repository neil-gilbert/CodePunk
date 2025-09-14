using System.Text.Json;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FluentAssertions;

namespace CodePunk.Console.Tests.Commands;

public class PlanGenerateAiProviderTests : ConsoleTestBase
{
    private class FakeProvider : ILLMProvider
    {
        public string Name => "Fake";
        public IReadOnlyList<LLMModel> Models { get; } = new [] { new LLMModel { Id = "fake-model", Name = "Fake Model" } };
        public Task<IReadOnlyList<LLMModel>> FetchModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Models);
        public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default)
        {
            var json = "{ \"files\": [ { \"path\": \"src/NewFile.cs\", \"action\": \"modify\", \"rationale\": \"Add new feature\" } ] }";
            return Task.FromResult(new LLMResponse { Content = json, Usage = new LLMUsage { InputTokens = 10, OutputTokens = 20, EstimatedCost = 0.0m } });
        }
        public async IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new LLMStreamChunk { Content = "{ \"files\": [", IsComplete = false };
            await Task.CompletedTask;
        }
    }

    private class FakeLLMService : ILLMService
    {
        private readonly FakeProvider _provider = new();
        public IReadOnlyList<ILLMProvider> GetProviders() => new [] { _provider };
        public ILLMProvider? GetProvider(string name) => name == _provider.Name ? _provider : null;
        public ILLMProvider GetDefaultProvider() => _provider;
        public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken ct = default) => _provider.SendAsync(request, ct);
        public IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, CancellationToken ct = default) => _provider.StreamAsync(request, ct);
    }

    [Fact]
    public void PlanGenerateAi_WithProviderModel_PersistsAndReturnsTokenUsage()
    {
        WithServices(s => {
            s.AddScoped<ILLMService, FakeLLMService>();
        });
        var output = Run("plan generate --ai --goal \"Implement feature X\" --provider Fake --model fake-model --json");
        var obj = JsonLast(output);
        obj.GetProperty("schema").GetString().Should().Be("plan.generate.ai.v1");
        obj.GetProperty("provider").GetString().Should().Be("Fake");
        obj.GetProperty("model").GetString().Should().Be("fake-model");
        var files = obj.GetProperty("files").EnumerateArray();
        files.Should().HaveCount(1);
        files.First().GetProperty("path").GetString().Should().Be("src/NewFile.cs");
    }
}
