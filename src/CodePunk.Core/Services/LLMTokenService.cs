using CodePunk.Core.Abstractions;
using CodePunk.Core.Providers.Anthropic;

namespace CodePunk.Core.Services;

/// <summary>
/// Provides token counting for LLM requests.
/// </summary>
public interface ILLMTokenService
{
    Task<int> CountTokensAsync(LLMRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default token counting service using provider-native endpoints when available.
/// </summary>
public sealed class LLMTokenService : ILLMTokenService
{
    private readonly ILLMService _llm;

    public LLMTokenService(ILLMService llm)
    {
        _llm = llm;
    }

    public async Task<int> CountTokensAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        var provider = _llm.GetDefaultProvider();
        if (provider is AnthropicProvider anthropic)
        {
            return await anthropic.CountTokensAsync(request, cancellationToken).ConfigureAwait(false);
        }
        return 0;
    }
}

