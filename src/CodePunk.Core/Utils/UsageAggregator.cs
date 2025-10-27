using System.Text;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;

namespace CodePunk.Core.Utils;

public static class UsageAggregator
{
    /// <summary>
    /// Produces a final LLMUsage for a streaming turn.
    /// If providerUsage is present, it is used for tokens and cost. Otherwise estimates tokens heuristically and cost via model pricing.
    /// </summary>
    public static LLMUsage BuildFinalUsage(
        ILLMProvider provider,
        string modelId,
        IReadOnlyList<Message> promptMessages,
        string? responseText,
        LLMUsage? providerUsage)
    {
        var model = provider.Models.FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase));
        decimal inRate = model?.CostPerInputToken ?? 0m;
        decimal outRate = model?.CostPerOutputToken ?? 0m;

        if (providerUsage != null)
        {
            var cost = (providerUsage.InputTokens * inRate) + (providerUsage.OutputTokens * outRate);
            return new LLMUsage
            {
                InputTokens = providerUsage.InputTokens,
                OutputTokens = providerUsage.OutputTokens,
                EstimatedCost = cost
            };
        }

        // Heuristic fallback: ~4 characters per token
        var promptTokens = EstimateTokens(promptMessages);
        var completionTokens = EstimateTokens(responseText ?? string.Empty);
        var estimatedCost = (promptTokens * inRate) + (completionTokens * outRate);
        return new LLMUsage
        {
            InputTokens = promptTokens,
            OutputTokens = completionTokens,
            EstimatedCost = estimatedCost
        };
    }

    private static int EstimateTokens(IEnumerable<Message> messages)
    {
        long chars = 0;
        foreach (var m in messages)
        {
            foreach (var part in m.Parts)
            {
                switch (part)
                {
                    case TextPart t:
                        chars += t.Content?.Length ?? 0;
                        break;
                    case ToolCallPart tc:
                        try { chars += tc.Arguments.GetRawText().Length; } catch { }
                        break;
                    case ToolResultPart tr:
                        chars += tr.Content?.Length ?? 0;
                        break;
                }
            }
        }
        return (int)Math.Ceiling(chars / 4.0);
    }

    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return (int)Math.Ceiling(text.Length / 4.0);
    }
}

