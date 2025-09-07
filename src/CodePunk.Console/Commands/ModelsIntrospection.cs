using System.Text;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Services;

// Internal helper solely for unit tests to verify model listing logic without relying on System.CommandLine console plumbing.
namespace CodePunk.Console.Commands;

public static class ModelsIntrospection
{
    public static string RenderModelsText(ILLMService llm, bool json)
    {
        var providers = llm.GetProviders() ?? Array.Empty<ILLMProvider>();
        var modelRows = providers
            .SelectMany(p => (p.Models ?? Array.Empty<LLMModel>()).Select(m => new { provider = p.Name, model = m }))
            .ToList();

        if (json)
        {
            var jsonRows = modelRows.Select(r => new
            {
                provider = r.provider,
                id = r.model.Id,
                name = r.model.Name,
                context = r.model.ContextWindow,
                max = r.model.MaxTokens,
                tools = r.model.SupportsTools,
                streaming = r.model.SupportsStreaming
            });
            return System.Text.Json.JsonSerializer.Serialize(jsonRows, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        if (providers.Count == 0)
            return "No providers available. Authenticate first: codepunk auth login --provider <name> --key <APIKEY>\n";

        if (modelRows.Count == 0)
            return "No models found.\n";

        var sb = new StringBuilder();
        foreach (var r in modelRows)
            sb.AppendLine($"{r.provider}\t{r.model.Id}\t{r.model.Name}");
        return sb.ToString();
    }
}
