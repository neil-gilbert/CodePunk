using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FluentAssertions;

namespace CodePunk.Console.Tests.Commands;

public class PlanGenerateAiTruncationTests : ConsoleTestBase
{
    private class LargeRationaleProvider : ILLMProvider
    {
        public string Name => "LargeRationale";
        public IReadOnlyList<LLMModel> Models { get; } = new [] { new LLMModel { Id = "lr-model", Name = "Large Rationale" } };
        public Task<IReadOnlyList<LLMModel>> FetchModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Models);
        public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default)
        {
            var longText = new string('A', 40_000); // exceeds default per-file cap 16384
            var json = $"{{ \"files\": [ {{ \"path\": \"src/Big.cs\", \"action\": \"modify\", \"rationale\": \"{longText}\" }} ] }}";
            return Task.FromResult(new LLMResponse { Content = json });
        }
        public async IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { yield break; }
    }

    private class LargeRationaleLLMService : ILLMService
    {
        private readonly LargeRationaleProvider _p = new();
        public IReadOnlyList<ILLMProvider> GetProviders() => new [] { _p };
        public ILLMProvider? GetProvider(string name) => name == _p.Name ? _p : null;
        public ILLMProvider GetDefaultProvider() => _p;
        public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default) => _p.SendAsync(request, cancellationToken);
        public IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, CancellationToken cancellationToken = default) => _p.StreamAsync(request, cancellationToken);
    }

    [Fact]
    public void PlanGenerateAi_LongRationale_Truncated()
    {
        WithServices(s => s.AddScoped<ILLMService, LargeRationaleLLMService>());
        var output = Run("plan generate --ai --goal \"Big rationale\" --provider LargeRationale --model lr-model --json");
        var obj = JsonLast(output);
        var rationale = obj.GetProperty("files").EnumerateArray().First().GetProperty("rationale").GetString();
        rationale!.Length.Should().BeLessOrEqualTo(17000); // includes ellipsis, rough guard
    }
}
