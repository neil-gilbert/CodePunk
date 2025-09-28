using System.Text.Json;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using CodePunk.Core.Models.FileEdit;
using CodePunk.Core.Services;

namespace CodePunk.ComponentTests.TestHelpers;

// Mock services that focus on behavior rather than implementation
public class MockSessionService : ISessionService
{
    private readonly Dictionary<string, Session> _sessions = new();

    public Task<Session> CreateAsync(string title, string? parentSessionId = null, CancellationToken cancellationToken = default)
    {
        var session = Session.Create(title);
        _sessions[session.Id] = session;
        return Task.FromResult(session);
    }

    public Task<Session?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        _sessions.TryGetValue(id, out var session);
        return Task.FromResult(session);
    }

    public Task<IReadOnlyList<Session>> GetRecentAsync(int count = 50, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Session>>(_sessions.Values.ToList());

    public Task<Session> UpdateAsync(Session session, CancellationToken cancellationToken = default) =>
        Task.FromResult(session);

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

public class MockMessageService : IMessageService
{
    private readonly List<Message> _messages = new();

    public Task<Message> CreateAsync(Message message, CancellationToken cancellationToken = default)
    {
        _messages.Add(message);
        return Task.FromResult(message);
    }

    public Task<IReadOnlyList<Message>> GetBySessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var sessionMessages = _messages.Where(m => m.SessionId == sessionId).ToList();
        return Task.FromResult<IReadOnlyList<Message>>(sessionMessages);
    }

    public Task<Message?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
        Task.FromResult<Message?>(null);

    public Task<Message> UpdateAsync(Message message, CancellationToken cancellationToken = default) =>
        Task.FromResult(message);

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task DeleteBySessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

public class MockToolService : IToolService
{
    public IReadOnlyList<ITool> GetTools() => new List<ITool>();
    public ITool? GetTool(string name) => null;
    public IReadOnlyList<LLMTool> GetLLMTools() => new List<LLMTool>();

    public Task<ToolResult> ExecuteAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        // Return success for component tests
        return Task.FromResult(new ToolResult { Content = $"Tool {toolName} executed successfully" });
    }
}

/// <summary>
/// Test approval service that can be configured for different test scenarios
/// </summary>
public class TestApprovalService : IApprovalService
{
    public bool AutoApprove { get; set; }
    public bool CancelAll { get; set; }

    public Task<ApprovalResult> RequestApprovalAsync(
        FileEditRequest request,
        string diff,
        DiffStats stats,
        CancellationToken cancellationToken = default)
    {
        if (CancelAll)
            return Task.FromResult(new ApprovalResult(false, "USER_CANCELLED"));

        if (AutoApprove)
            return Task.FromResult(new ApprovalResult(true));

        // Default to approval for tests
        return Task.FromResult(new ApprovalResult(true));
    }
}