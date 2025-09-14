using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Xunit;
using FluentAssertions;

namespace CodePunk.Console.Tests.Commands;

public class PlanGenerateAiConfigOverrideTests : ConsoleTestBase
{
    private class TwoFilesProvider : ILLMProvider
    {
        public string Name => "TwoFiles";
        public IReadOnlyList<LLMModel> Models { get; } = new [] { new LLMModel { Id = "two-model", Name = "Two" } };
        public Task<IReadOnlyList<LLMModel>> FetchModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Models);
        public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default)
        {
            var json = "{ \"files\": [ { \"path\": \"a.txt\", \"action\": \"modify\" }, { \"path\": \"b.txt\", \"action\": \"modify\" } ] }";
            return Task.FromResult(new LLMResponse { Content = json });
        }
        public async IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { yield break; }
    }

    private class TwoFilesLLMService : ILLMService
    {
        private readonly TwoFilesProvider _p = new();
        public IReadOnlyList<ILLMProvider> GetProviders() => new [] { _p };
        public ILLMProvider? GetProvider(string name) => name == _p.Name ? _p : null;
        public ILLMProvider GetDefaultProvider() => _p;
        public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default) => _p.SendAsync(request, cancellationToken);
        public IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, CancellationToken cancellationToken = default) => _p.StreamAsync(request, cancellationToken);
    }

    [Fact]
    public void PlanGenerateAi_ConfigOverride_MaxFiles1_TriggersTooMany()
    {
        WithServices(s => {
            s.AddScoped<ILLMService, TwoFilesLLMService>();
            // Add in-memory config override
            var dict = new Dictionary<string,string?>
            {
                {"PlanAI:MaxFiles", "1"}
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
            s.AddSingleton<IConfiguration>(sp => {
                // merge original config if needed: just return new for test
                return config;
            });
        });
        var output = Run("plan generate --ai --goal \"Two files\" --provider TwoFiles --model two-model --json");
        var obj = JsonLast(output);
        obj.GetProperty("error").GetProperty("code").GetString().Should().Be("TooManyFiles");
    }
}
