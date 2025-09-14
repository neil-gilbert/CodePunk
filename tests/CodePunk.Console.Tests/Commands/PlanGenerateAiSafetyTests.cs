using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FluentAssertions;
using System.Text.Json;

namespace CodePunk.Console.Tests.Commands;

public class PlanGenerateAiSafetyTests : ConsoleTestBase
{
    private class ManyFilesProvider : ILLMProvider
    {
        public string Name => "Many";
        public IReadOnlyList<LLMModel> Models { get; } = new [] { new LLMModel { Id = "many-model", Name = "Many" } };
        public Task<IReadOnlyList<LLMModel>> FetchModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Models);
        public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default)
        {
            var files = string.Join(',', Enumerable.Range(0, 25).Select(i => $"{{ \"path\": \"file{i}.txt\", \"action\": \"modify\" }}"));
            var json = $"{{ \"files\": [ {files} ] }}";
            return Task.FromResult(new LLMResponse { Content = json });
        }
        public async IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { yield break; }
    }

    private class UnsafePathProvider : ILLMProvider
    {
        public string Name => "Unsafe";
        public IReadOnlyList<LLMModel> Models { get; } = new [] { new LLMModel { Id = "unsafe-model", Name = "Unsafe" } };
        public Task<IReadOnlyList<LLMModel>> FetchModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Models);
        public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default)
        {
            var json = "{ \"files\": [ { \"path\": \"../secrets.txt\", \"action\": \"modify\", \"rationale\": \"API_KEY=123\" } ] }";
            return Task.FromResult(new LLMResponse { Content = json });
        }
        public async IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { yield break; }
    }

    private class ManyLLMService : ILLMService
    {
        private readonly ManyFilesProvider _p = new();
        public IReadOnlyList<ILLMProvider> GetProviders() => new [] { _p };
        public ILLMProvider? GetProvider(string name) => name == _p.Name ? _p : null;
        public ILLMProvider GetDefaultProvider() => _p;
        public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default) => _p.SendAsync(request, cancellationToken);
        public Task<LLMResponse> SendAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default) => _p.SendAsync(request, cancellationToken);
        public IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, CancellationToken cancellationToken = default) => _p.StreamAsync(request, cancellationToken);
        public IAsyncEnumerable<LLMStreamChunk> StreamAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default) => _p.StreamAsync(request, cancellationToken);
        public Task<Message> SendMessageAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<LLMStreamChunk> SendMessageStreamAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private class UnsafeLLMService : ILLMService
    {
        private readonly UnsafePathProvider _p = new();
        public IReadOnlyList<ILLMProvider> GetProviders() => new [] { _p };
        public ILLMProvider? GetProvider(string name) => name == _p.Name ? _p : null;
        public ILLMProvider GetDefaultProvider() => _p;
        public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default) => _p.SendAsync(request, cancellationToken);
        public Task<LLMResponse> SendAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default) => _p.SendAsync(request, cancellationToken);
        public IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, CancellationToken cancellationToken = default) => _p.StreamAsync(request, cancellationToken);
        public IAsyncEnumerable<LLMStreamChunk> StreamAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default) => _p.StreamAsync(request, cancellationToken);
        public Task<Message> SendMessageAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<LLMStreamChunk> SendMessageStreamAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    [Fact]
    public void PlanGenerateAi_ToManyFiles_Error()
    {
        WithServices(s => s.AddScoped<ILLMService, ManyLLMService>());
        var output = Run("plan generate --ai --goal \"Large\" --provider Many --model many-model --json");
        var obj = JsonLast(output);
        obj.GetProperty("error").GetProperty("code").GetString().Should().Be("TooManyFiles");
    }

    [Fact]
    public void PlanGenerateAi_UnsafePathAndSecret_RedactedAndFlagged()
    {
        WithServices(s => s.AddScoped<ILLMService, UnsafeLLMService>());
        var output = Run("plan generate --ai --goal \"Unsafe\" --provider Unsafe --model unsafe-model --json");
        var obj = JsonLast(output);
        obj.GetProperty("files").EnumerateArray().First().GetProperty("rationale").GetString().Should().Contain("<REDACTED>");
    }
}
