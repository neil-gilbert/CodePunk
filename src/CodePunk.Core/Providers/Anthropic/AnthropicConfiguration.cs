using System;

namespace CodePunk.Core.Providers.Anthropic;

public class AnthropicConfiguration
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1";
    public string DefaultModel { get; set; } = AnthropicModels.ClaudeOpus41;
    public int MaxTokens { get; set; } = 4096;
    public double Temperature { get; set; } = 0.7;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);
    public string Version { get; set; } = "2023-06-01";
}
