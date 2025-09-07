using CodePunk.Console.Chat;
using CodePunk.Console.Commands;
using CodePunk.Console.Rendering;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Chat;
using CodePunk.Core.Models;
using CodePunk.Core.Services;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CodePunk.Console.Tests;

public class DependencyResolutionTests
{
    [Fact]
    public async Task InteractiveChatLoop_and_RootCommand_should_resolve()
    {
        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
        builder.Services.AddLogging();
        builder.Services.AddSingleton<Spectre.Console.IAnsiConsole>(Spectre.Console.AnsiConsole.Console);
        builder.Services.AddSingleton<StreamingResponseRenderer>();
        builder.Services.AddSingleton<CommandProcessor>();
        builder.Services.AddSingleton<IChatSessionOptions>(new ChatSessionOptions());

        // Stub domain/services
        builder.Services.AddSingleton<ISessionService, StubSessionService>();
        builder.Services.AddSingleton<IMessageService, StubMessageService>();
        builder.Services.AddSingleton<ILLMService, StubLLMService>();
        builder.Services.AddSingleton<IToolService, StubToolService>();

        // Chat core
        builder.Services.AddScoped<InteractiveChatSession>();
        builder.Services.AddScoped<InteractiveChatLoop>();

        // Commands
        builder.Services.AddTransient<ChatCommand, HelpCommand>();
        builder.Services.AddTransient<ChatCommand, NewCommand>();
        builder.Services.AddTransient<ChatCommand, QuitCommand>();
        builder.Services.AddTransient<ChatCommand, ClearCommand>();
        builder.Services.AddTransient<ChatCommand, SessionsCommand>();
        builder.Services.AddTransient<ChatCommand, LoadCommand>();

        using var host = builder.Build();
        using var scope = host.Services.CreateScope();
        var loop = scope.ServiceProvider.GetRequiredService<InteractiveChatLoop>();
        Assert.NotNull(loop);
        var root = RootCommandFactory.Create(scope.ServiceProvider);
        Assert.NotNull(root);
        await Task.CompletedTask;
    }
}

#region Stub Implementations
internal class StubSessionService : ISessionService
{
    private readonly List<Session> _sessions = new();
    public Task<Session?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<Session?>(_sessions.FirstOrDefault(s => s.Id == id));
    public Task<IReadOnlyList<Session>> GetRecentAsync(int count = 50, CancellationToken cancellationToken = default) => Task.FromResult((IReadOnlyList<Session>)_sessions.Take(count).ToList());
    public Task<Session> CreateAsync(string title, string? parentSessionId = null, CancellationToken cancellationToken = default)
    {
        var s = Session.Create(title, parentSessionId);
        _sessions.Add(s);
        return Task.FromResult(s);
    }
    public Task<Session> UpdateAsync(Session session, CancellationToken cancellationToken = default) => Task.FromResult(session);
    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    { _sessions.RemoveAll(s => s.Id == id); return Task.CompletedTask; }
}
internal class StubMessageService : IMessageService
{
    private readonly List<Message> _messages = new();
    public Task<Message?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<Message?>(_messages.FirstOrDefault(m => m.Id == id));
    public Task<IReadOnlyList<Message>> GetBySessionAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult((IReadOnlyList<Message>)_messages.Where(m => m.SessionId == sessionId).ToList());
    public Task<Message> CreateAsync(Message message, CancellationToken cancellationToken = default)
    { _messages.Add(message); return Task.FromResult(message); }
    public Task<Message> UpdateAsync(Message message, CancellationToken cancellationToken = default) => Task.FromResult(message);
    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    { _messages.RemoveAll(m => m.Id == id); return Task.CompletedTask; }
    public Task DeleteBySessionAsync(string sessionId, CancellationToken cancellationToken = default)
    { _messages.RemoveAll(m => m.SessionId == sessionId); return Task.CompletedTask; }
}
internal class StubLLMService : ILLMService
{
    public IReadOnlyList<ILLMProvider> GetProviders() => Array.Empty<ILLMProvider>();
    public ILLMProvider? GetProvider(string name) => null;
    public ILLMProvider GetDefaultProvider() => new StubProvider();
    public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new LLMResponse { Content = "stub" });
    public Task<LLMResponse> SendAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default) => SendAsync(request, cancellationToken);
    public IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, CancellationToken cancellationToken = default) => AsyncEmpty();
    public IAsyncEnumerable<LLMStreamChunk> StreamAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default) => AsyncEmpty();
    public Task<Message> SendMessageAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default) => Task.FromResult(Message.Create(conversationHistory.Last().SessionId, MessageRole.Assistant, [new TextPart("stub")]) );
    public IAsyncEnumerable<LLMStreamChunk> SendMessageStreamAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default) => AsyncEmpty();
    private IAsyncEnumerable<LLMStreamChunk> AsyncEmpty() => Empty();
    private async IAsyncEnumerable<LLMStreamChunk> Empty() { await Task.CompletedTask; yield break; }
    private class StubProvider : ILLMProvider 
    { 
        public string Name => "stub"; 
        public IReadOnlyList<LLMModel> Models => Array.Empty<LLMModel>();
        public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new LLMResponse { Content = "stub" }); 
    public IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, CancellationToken cancellationToken = default) => Stream(); 
    private async IAsyncEnumerable<LLMStreamChunk> Stream(){ await Task.CompletedTask; yield break; }
    public Task<IReadOnlyList<LLMModel>> FetchModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Models);
    }
}
internal class StubToolService : IToolService
{
    public IReadOnlyList<ITool> GetTools() => Array.Empty<ITool>();
    public ITool? GetTool(string name) => null;
    public Task<ToolResult> ExecuteAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken = default) => Task.FromResult(new ToolResult { Content = "ok" });
    public IReadOnlyList<LLMTool> GetLLMTools() => Array.Empty<LLMTool>();
}
#endregion
