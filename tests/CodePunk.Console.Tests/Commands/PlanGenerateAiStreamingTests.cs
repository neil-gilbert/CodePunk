using System.Threading;
using System.Threading.Tasks;
using CodePunk.Console.Planning;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using CodePunk.Console.Stores;
using CodePunk.Core.Services;
using Microsoft.Extensions.Options;
using Xunit;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CodePunk.Console.Tests.Commands;

public class PlanGenerateAiStreamingTests
{
    private class InMemoryStore : CodePunk.Console.Stores.IPlanFileStore
    {
        private readonly Dictionary<string, CodePunk.Console.Stores.PlanRecord> _store = new();
        public Task<string> CreateAsync(string goal, CancellationToken ct = default)
        {
            var id = "id-stream";
            _store[id] = new CodePunk.Console.Stores.PlanRecord { Definition = new PlanDefinition { Id = id, Goal = goal } };
            return Task.FromResult(id);
        }
        public Task<CodePunk.Console.Stores.PlanRecord?> GetAsync(string id, CancellationToken ct = default)
        {
            _store.TryGetValue(id, out var r);
            return Task.FromResult(r);
        }
        public Task<IReadOnlyList<PlanDefinition>> ListAsync(int? take = null, CancellationToken ct = default)
        {
            var list = new List<PlanDefinition>();
            foreach (var kv in _store)
            {
                list.Add(kv.Value.Definition);
            }
            return Task.FromResult((IReadOnlyList<PlanDefinition>)list);
        }
        public Task SaveAsync(CodePunk.Console.Stores.PlanRecord record, CancellationToken ct = default)
        {
            _store[record.Definition.Id] = record;
            return Task.CompletedTask;
        }
    }

    private class TestStreamingProvider : ILLMProvider
    {
        public string Name => "TestStreaming";
        public IReadOnlyList<LLMModel> Models { get; } = new[] { new LLMModel { Id = "test", Name = "test", SupportsStreaming = true } };

        public Task<IReadOnlyList<LLMModel>> FetchModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Models);

        public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default)
        {
            var content = "{\"files\":[{\"path\":\"stream.txt\",\"rationale\":\"from-sync\"}]}";
            return Task.FromResult(new LLMResponse { Content = content });
        }

        public async IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var part1 = "{\"files\":[{\"path\":\"stream.txt\",\"rationale\":\"from-";
            var part2 = "stream\"}]}";
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            yield return new LLMStreamChunk { Content = part1 };
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            yield return new LLMStreamChunk { Content = part2, IsComplete = true };
        }
    }

    private class SimpleLLMService : CodePunk.Core.Services.ILLMService
    {
        private readonly ILLMProvider _prov;
        public SimpleLLMService(ILLMProvider p) { _prov = p; }
        public IReadOnlyList<ILLMProvider> GetProviders() => new[] { _prov };
        public ILLMProvider? GetProvider(string name) => name == _prov.Name ? _prov : null;
        public ILLMProvider GetDefaultProvider() => _prov;
        public void SetSessionDefaults(string? providerName, string? modelId) { }
        public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default) => _prov.SendAsync(request, cancellationToken);
        public Task<LLMResponse> SendAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default) => _prov.SendAsync(request, cancellationToken);
        public IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, CancellationToken cancellationToken = default) => _prov.StreamAsync(request, cancellationToken);
        public IAsyncEnumerable<LLMStreamChunk> StreamAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default) => _prov.StreamAsync(request, cancellationToken);
        public Task<Message> SendMessageAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<LLMStreamChunk> SendMessageStreamAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CountTokensAsync(LLMRequest request, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    [Fact]
    public async Task GenerateAsync_UsesStreamingWhenAvailable()
    {
        var store = new InMemoryStore();
        var provider = new TestStreamingProvider();
        var llm = new SimpleLLMService(provider);
        var opts = Options.Create(new PlanAiGenerationOptions());
        var svc = new PlanAiGenerationService(store, llm, opts);

        var res = await svc.GenerateAsync("goal", null, null, CancellationToken.None);
        Assert.NotNull(res);
        Assert.Equal("TestStreaming", res.Provider);
        Assert.Single(res.Files);
        Assert.Equal("stream.txt", res.Files[0].Path);
        Assert.Equal("from-stream", res.Files[0].Rationale);
    }
}
