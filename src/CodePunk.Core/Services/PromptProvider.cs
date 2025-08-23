using System.Reflection;
using CodePunk.Core.Models;

namespace CodePunk.Core.Services;

/// <summary>
/// Provides system prompts optimized for different LLM providers
/// </summary>
public class PromptProvider : IPromptProvider
{
    private readonly Dictionary<string, Dictionary<PromptType, string>> _prompts;

    public PromptProvider()
    {
        _prompts = new Dictionary<string, Dictionary<PromptType, string>>(StringComparer.OrdinalIgnoreCase);
        LoadPrompts();
    }

    public string GetSystemPrompt(string providerName, PromptType promptType = PromptType.Coder)
    {
        // Try to get provider-specific prompt first
        if (_prompts.TryGetValue(providerName, out var providerPrompts) &&
            providerPrompts.TryGetValue(promptType, out var prompt))
        {
            return prompt;
        }

        // Fall back to OpenAI prompts as default
        if (_prompts.TryGetValue("OpenAI", out var defaultPrompts) &&
            defaultPrompts.TryGetValue(promptType, out var defaultPrompt))
        {
            return defaultPrompt;
        }

        // Final fallback to basic prompt
        return GetBasicPrompt(promptType);
    }

    public IEnumerable<PromptType> GetAvailablePromptTypes(string providerName)
    {
        if (_prompts.TryGetValue(providerName, out var providerPrompts))
        {
            return providerPrompts.Keys;
        }

        return new[] { PromptType.Coder };
    }

    private void LoadPrompts()
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

                // Parse resource name: CodePunk.Core.Prompts.OpenAI.Coder.md
                var parts = resourceName.Split('.');
                if (parts.Length >= 4)
                {
                    var providerName = parts[^3]; // Third from last
                    var promptTypeName = parts[^2]; // Second from last

                    if (Enum.TryParse<PromptType>(promptTypeName, true, out var promptType))
                    {
                        if (!_prompts.ContainsKey(providerName))
                        {
                            _prompts[providerName] = new Dictionary<PromptType, string>();
                        }

                        _prompts[providerName][promptType] = content;
                    }
                }
            }
            catch (Exception)
            {
                // Continue loading other prompts if one fails
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
