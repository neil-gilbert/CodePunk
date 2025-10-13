using System.Reflection;
using CodePunk.Core.Models;
using Microsoft.Extensions.Logging;

namespace CodePunk.Core.Services;

/// <summary>
/// Provides system prompts optimized for different LLM providers
/// </summary>
public class PromptProvider : IPromptProvider
{
    private readonly Dictionary<string, Dictionary<PromptType, string>> _providerPrompts; // provider -> (type -> content)
    private readonly Dictionary<PromptType, string> _basePrompts; // base layer
    private readonly Dictionary<(string provider, PromptType type), string> _compositeCache; // resolved combinations
    private readonly ILogger<PromptProvider>? _logger;

    public PromptProvider(ILogger<PromptProvider>? logger = null)
    {
        _logger = logger;
        _providerPrompts = new Dictionary<string, Dictionary<PromptType, string>>(StringComparer.OrdinalIgnoreCase);
        _basePrompts = new Dictionary<PromptType, string>();
        _compositeCache = new Dictionary<(string provider, PromptType type), string>();
        LoadEmbeddedPrompts();
        LoadExternalPromptDirectories();
    }

    public string GetSystemPrompt(string providerName, PromptType promptType = PromptType.Coder)
    {
        var key = (providerName, promptType);
        if (_compositeCache.TryGetValue(key, out var cached)) return cached;

        string? baseLayer = _basePrompts.TryGetValue(promptType, out var bp) ? bp : null;
        string? providerLayer = null;

        if (_providerPrompts.TryGetValue(providerName, out var providerMap) && providerMap.TryGetValue(promptType, out var pl))
        {
            providerLayer = pl;
        }
        else if (_providerPrompts.TryGetValue("OpenAI", out var defaultMap) && defaultMap.TryGetValue(promptType, out var dp))
        {
            providerLayer = dp;
        }

        // Composition strategy can be tuned via env var: CODEPUNK_PROMPT_COMPOSE = provider | base | composite
        var compose = Environment.GetEnvironmentVariable("CODEPUNK_PROMPT_COMPOSE")?.Trim().ToLowerInvariant();
        string resolved;
        if (compose == "provider" && providerLayer != null)
        {
            resolved = providerLayer.TrimEnd() + "\n";
        }
        else if (compose == "base" && baseLayer != null)
        {
            resolved = baseLayer.TrimEnd() + "\n";
        }
        else if (baseLayer != null && providerLayer != null)
        {
            resolved = baseLayer.TrimEnd() + "\n\n" + providerLayer.Trim() + "\n";
        }
        else if (providerLayer != null)
        {
            resolved = providerLayer.TrimEnd() + "\n";
        }
        else if (baseLayer != null)
        {
            resolved = baseLayer.TrimEnd() + "\n";
        }
        else
        {
            resolved = GetBasicPrompt(promptType);
        }

        _compositeCache[key] = resolved;
        return resolved;
    }

    public IEnumerable<PromptType> GetAvailablePromptTypes(string providerName)
    {
        var set = new HashSet<PromptType>();
        if (_basePrompts.Count > 0)
            foreach (var t in _basePrompts.Keys) set.Add(t);
        if (_providerPrompts.TryGetValue(providerName, out var providerMap))
            foreach (var t in providerMap.Keys) set.Add(t);
        if (set.Count == 0) set.Add(PromptType.Coder);
        return set;
    }
    private void LoadEmbeddedPrompts()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.Contains("Prompts.") && name.EndsWith(".md"))
            .ToList();

        foreach (var resourceName in resourceNames)
        {
            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) continue;
                using var reader = new StreamReader(stream);
                var content = reader.ReadToEnd();
                var parts = resourceName.Split('.');
                if (parts.Length >= 4)
                {
                    var providerName = parts[^3]; // e.g., OpenAI / Anthropic / Base
                    var promptTypeName = parts[^2];
                    if (Enum.TryParse<PromptType>(promptTypeName, true, out var promptType))
                    {
                        if (string.Equals(providerName, "Base", StringComparison.OrdinalIgnoreCase))
                        {
                            _basePrompts[promptType] = content;
                        }
                        else
                        {
                            if (!_providerPrompts.ContainsKey(providerName))
                                _providerPrompts[providerName] = new Dictionary<PromptType, string>();
                            _providerPrompts[providerName][promptType] = content;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load embedded prompt {Resource}", resourceName);
            }
        }
    }

    private void LoadExternalPromptDirectories()
    {
        var env = Environment.GetEnvironmentVariable("CODEPUNK_PROMPT_PATHS");
        if (string.IsNullOrWhiteSpace(env)) return;
        var paths = env.Split(new[] { ':', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var path in paths)
        {
            try
            {
                if (!Directory.Exists(path)) continue;
                foreach (var file in Directory.EnumerateFiles(path, "*.md", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file); // Provider.PromptType
                        var parts = fileName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (parts.Length == 2 && Enum.TryParse<PromptType>(parts[1], true, out var pt))
                        {
                            var provider = parts[0];
                            var content = File.ReadAllText(file);
                            if (string.Equals(provider, "Base", StringComparison.OrdinalIgnoreCase))
                            {
                                _basePrompts[pt] = content; // external base overrides embedded
                            }
                            else
                            {
                                if (!_providerPrompts.ContainsKey(provider))
                                    _providerPrompts[provider] = new Dictionary<PromptType, string>();
                                _providerPrompts[provider][pt] = content; // override
                            }
                        }
                    }
                    catch (Exception exFile)
                    {
                        _logger?.LogWarning(exFile, "Failed to load prompt file {File}", file);
                    }
                }
            }
            catch (Exception exDir)
            {
                _logger?.LogWarning(exDir, "Failed processing prompt directory {Dir}", path);
            }
        }
    }

    private static string GetBasicPrompt(PromptType promptType)
    {
        return promptType switch
        {
            PromptType.Coder => @"You are CodePunk, an agentic coding assistant that helps engineers with software development tasks.

# Core Capabilities
- Code analysis, generation, and refactoring across all programming languages
- File operations (read, write, modify) with context awareness
- Shell command execution for testing, building, and deployment
- Project understanding and architectural guidance
- Debugging and error resolution

# Interaction Guidelines
- Be direct and concise in responses
- Execute actions proactively when requested
- Maintain conversation context across sessions
- Prioritize code quality and best practices
- Ask for clarification only when necessary

# Tool Usage
- Use file operations to read and modify code
- Execute shell commands for testing and validation
- Analyze project structure before making changes
- Follow existing code conventions and patterns

You are here to make software development more efficient and enjoyable.",

            PromptType.Title => @"Generate a concise title for the conversation based on the user's first message.

Requirements:
- Maximum 50 characters
- No quotes or colons  
- One line only
- Summarize the main topic or task
- Use present tense when possible",

            PromptType.Task => @"You are a task completion agent for CodePunk. Analyze the user's request and break it down into actionable steps.

Focus on:
- Understanding the specific requirements
- Identifying files and systems involved
- Planning the sequence of operations
- Anticipating potential issues

Provide clear, executable steps to complete the task.",

            PromptType.Summarizer => @"Summarize the conversation or content concisely while preserving key technical details.

Include:
- Main topics discussed
- Key decisions made
- Code changes implemented
- Outstanding issues or next steps

Keep summaries factual and technically accurate.",

            _ => "You are a helpful coding assistant."
        };
    }
}
