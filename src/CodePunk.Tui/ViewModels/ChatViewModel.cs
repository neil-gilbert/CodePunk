using System.Collections.ObjectModel;
using System.Text;
using CodePunk.Core.Chat;
using Microsoft.Extensions.Logging;
using CodePunk.Tui.Components;

namespace CodePunk.Tui.ViewModels;

public sealed class ChatViewModel
{
    private readonly InteractiveChatSession _chat;
    private readonly ILogger<ChatViewModel> _logger;

    private readonly ObservableCollection<ConversationItem> _conversationItems = new();

    public IReadOnlyList<ConversationItem> ConversationItems => _conversationItems;
    public string Input { get; set; } = string.Empty;
    public bool IsProcessing { get; private set; }
    public string LiveBuffer { get; private set; } = string.Empty;
    public event Action? Changed;

    public ChatViewModel(InteractiveChatSession chat, ILogger<ChatViewModel> logger)
    {
        _chat = chat;
        _logger = logger;
    }

    public async Task EnsureSessionAsync()
    {
        if (_chat.IsActive) return;
        var title = $"Chat {DateTime.Now:yyyy-MM-dd HH:mm}";
        try { await _chat.StartNewSessionAsync(title); } catch { }
    }

    public async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(Input)) return;

        var text = Input.Trim();
        Input = string.Empty;
        Changed?.Invoke(); // Notify UI that input was cleared

        _conversationItems.Add(new UserMessageItem { Content = text });
        IsProcessing = true;
        LiveBuffer = string.Empty;
        Changed?.Invoke();
        var sb = new StringBuilder();

        try
        {
            var chunkCount = 0;
            await foreach (var chunk in _chat.SendMessageStreamAsync(text))
            {
                if (ToolStatusSerializer.TryDeserialize(chunk.ContentDelta, out var status) && status != null)
                {
                    _conversationItems.Add(new ToolExecutionItem
                    {
                        ToolName = status.ToolName,
                        FilePath = status.FilePath,
                        Preview = status.Preview ?? string.Empty,
                        IsTruncated = status.IsTruncated,
                        OriginalLineCount = status.OriginalLineCount,
                        MaxLines = status.MaxLines,
                        IsError = status.IsError,
                        LanguageId = status.LanguageId
                    });
                    Changed?.Invoke();
                    continue;
                }

                if (!string.IsNullOrEmpty(chunk.ContentDelta))
                {
                    sb.Append(chunk.ContentDelta);
                    LiveBuffer = sb.ToString();

                    // Only update UI every 5 chunks to reduce render frequency
                    chunkCount++;
                    if (chunkCount % 5 == 0)
                    {
                        Changed?.Invoke();
                    }
                }
            }

            // Final update to ensure we show the complete buffer
            if (chunkCount % 5 != 0)
            {
                Changed?.Invoke();
            }

            var finalContent = sb.ToString();
            if (!string.IsNullOrWhiteSpace(finalContent))
            {
                _conversationItems.Add(new AssistantMessageItem { Content = finalContent });
                Changed?.Invoke();
            }
        }
        catch (Exception ex)
        {
            _conversationItems.Add(new ErrorMessageItem { Content = $"Error: {ex.Message}" });
            Changed?.Invoke();
        }
        finally
        {
            IsProcessing = false;
            LiveBuffer = string.Empty;
            Changed?.Invoke();
        }
    }
}

// Unified conversation item model
public abstract record ConversationItem;

public sealed record UserMessageItem : ConversationItem
{
    public required string Content { get; init; }
}

public sealed record AssistantMessageItem : ConversationItem
{
    public required string Content { get; init; }
}

public sealed record ErrorMessageItem : ConversationItem
{
    public required string Content { get; init; }
}

public sealed record ToolExecutionItem : ConversationItem
{
    public required string ToolName { get; init; }
    public string? FilePath { get; init; }
    public string Preview { get; init; } = string.Empty;
    public bool IsTruncated { get; init; }
    public int OriginalLineCount { get; init; }
    public int MaxLines { get; init; }
    public bool IsError { get; init; }
    public string? LanguageId { get; init; }
}

public sealed record DiffItem : ConversationItem
{
    public required string Diff { get; init; }
    public string? FilePath { get; init; }
}
