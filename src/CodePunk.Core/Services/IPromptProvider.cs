using CodePunk.Core.Models;

namespace CodePunk.Core.Services;

/// <summary>
/// Provides system prompts optimized for different LLM providers
/// </summary>
public interface IPromptProvider
{
    /// <summary>
    /// Get the system prompt for a specific provider
    /// </summary>
    string GetSystemPrompt(string providerName, PromptType promptType = PromptType.Coder);
    
    /// <summary>
    /// Get available prompt types for a provider
    /// </summary>
    IEnumerable<PromptType> GetAvailablePromptTypes(string providerName);
}

/// <summary>
/// Types of prompts available for different use cases
/// </summary>
public enum PromptType
{
    /// <summary>
    /// Main coding assistant prompt
    /// </summary>
    Coder,
    
    /// <summary>
    /// Session title generation prompt
    /// </summary>
    Title,
    
    /// <summary>
    /// Task completion prompt
    /// </summary>
    Task,
    
    /// <summary>
    /// Content summarization prompt
    /// </summary>
    Summarizer
}
