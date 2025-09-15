using System.Text;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;

namespace CodePunk.Tests.TestProviders;

internal class TestStreamingProvider : ILLMProvider
{
    public string Name => "TestStreaming";
    public IReadOnlyList<LLMModel> Models { get; } = new[] { new LLMModel { Id = "test", Name = "test", SupportsStreaming = true } };

    public Task<IReadOnlyList<LLMModel>> FetchModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Models);

    public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        // Return a synchronous full JSON
        var content = "{\"files\":[{\"path\":\"stream.txt\",\"rationale\":\"from-sync\"}]}";
        return Task.FromResult(new LLMResponse { Content = content });
    }

    public async IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // yield partial content split across two chunks
        var part1 = "{\"files\":[{\"path\":\"stream.txt\",\"rationale\":\"from-";
        var part2 = "stream\"}]}";
        await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        yield return new LLMStreamChunk { Content = part1 };
        await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        yield return new LLMStreamChunk { Content = part2, IsComplete = true };
    }
}
