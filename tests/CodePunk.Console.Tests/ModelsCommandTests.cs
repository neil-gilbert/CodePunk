using System.CommandLine;
using System.CommandLine.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using CodePunk.Console.Commands;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using CodePunk.Core.Services;
using CodePunk.Core.Chat;
using CodePunk.Console.Chat;
using CodePunk.Console.Rendering;
using Spectre.Console;
using CodePunk.Console.Stores;
using System.Text.Json;

namespace CodePunk.Console.Tests;

public class ModelsCommandTests
{
    [Fact]
    public void Models_no_providers_outputs_guidance()
    {
    var llm = new EmptyLLMService();
    var text = ModelsIntrospection.RenderModelsText(llm, json:false);
    Assert.Contains("No providers", text);
    }

    [Fact]
    public void Models_with_providers_table_output()
    {
    var llm = new SampleLLMService();
    var outText = ModelsIntrospection.RenderModelsText(llm, json:false);
        Assert.Contains("providerA", outText);
        Assert.Contains("model-a1", outText);
        Assert.DoesNotContain("{\"provider\"", outText); // not JSON
    }

    [Fact]
    public void Models_with_providers_json_output()
    {
    var llm = new SampleLLMService();
    var outText = ModelsIntrospection.RenderModelsText(llm, json:true);
        Assert.StartsWith("[", outText.Trim());
        using var doc = JsonDocument.Parse(outText);
        Assert.Equal("providerA", doc.RootElement[0].GetProperty("provider").GetString());
    }

    // Removed complex test DI & console capture in favor of direct model introspection.

    #region Stubs
    private class EmptyLLMService : ILLMService
    {
        public IReadOnlyList<ILLMProvider> GetProviders() => Array.Empty<ILLMProvider>();
        public ILLMProvider? GetProvider(string name) => null;
        public ILLMProvider GetDefaultProvider() => new Provider();
        public void SetSessionDefaults(string? providerName, string? modelId) { }
    public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new LLMResponse { Content = string.Empty });
    public Task<LLMResponse> SendAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new LLMResponse { Content = string.Empty });
        public IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, CancellationToken cancellationToken = default) => AsyncEmpty();
        public IAsyncEnumerable<LLMStreamChunk> StreamAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default) => AsyncEmpty();
    public Task<Message> SendMessageAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default) => Task.FromResult(Message.Create("s", MessageRole.Assistant, new List<MessagePart> { new TextPart("ok") }));
        public IAsyncEnumerable<LLMStreamChunk> SendMessageStreamAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default) => AsyncEmpty();
        private IAsyncEnumerable<LLMStreamChunk> AsyncEmpty() => Empty();
        private async IAsyncEnumerable<LLMStreamChunk> Empty() { await Task.CompletedTask; yield break; }
    private class Provider : ILLMProvider {
        public string Name => "empty";
        public IReadOnlyList<LLMModel> Models => Array.Empty<LLMModel>();
        public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new LLMResponse { Content = string.Empty });
        public IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, CancellationToken cancellationToken = default) => Empty();
        private async IAsyncEnumerable<LLMStreamChunk> Empty(){ await Task.CompletedTask; yield break; }
        public Task<IReadOnlyList<LLMModel>> FetchModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Models);
    }
    }
    private class SampleLLMService : ILLMService
    {
        private readonly IReadOnlyList<ILLMProvider> _providers = new ILLMProvider[]{ new Provider("providerA", new[]{ new LLMModel{ Id="model-a1", Name="Model A1", ContextWindow=1000, MaxTokens=500, SupportsTools=true, SupportsStreaming=true } }), new Provider("providerB", new[]{ new LLMModel{ Id="model-b1", Name="Model B1", ContextWindow=2000, MaxTokens=800 } }) };
        public IReadOnlyList<ILLMProvider> GetProviders() => _providers;
        public ILLMProvider? GetProvider(string name) => _providers.FirstOrDefault(p => p.Name==name);
        public ILLMProvider GetDefaultProvider() => _providers[0];
        public void SetSessionDefaults(string? providerName, string? modelId) { }
    public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new LLMResponse { Content = string.Empty });
    public Task<LLMResponse> SendAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new LLMResponse { Content = string.Empty });
        public IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, CancellationToken cancellationToken = default) => Empty();
        public IAsyncEnumerable<LLMStreamChunk> StreamAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default) => Empty();
    public Task<Message> SendMessageAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default) => Task.FromResult(Message.Create("s", MessageRole.Assistant, new List<MessagePart>{ new TextPart("ok") }));
        public IAsyncEnumerable<LLMStreamChunk> SendMessageStreamAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default) => Empty();
        private async IAsyncEnumerable<LLMStreamChunk> Empty() { await Task.CompletedTask; yield break; }
    private record Provider(string Name, IReadOnlyList<LLMModel> Models) : ILLMProvider {
        public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new LLMResponse { Content = string.Empty });
        public IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, CancellationToken cancellationToken = default) => Empty();
        private async IAsyncEnumerable<LLMStreamChunk> Empty(){ await Task.CompletedTask; yield break; }
        public Task<IReadOnlyList<LLMModel>> FetchModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Models);
    }
    }
    private class StubSessionService : ISessionService { public Task<Session?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<Session?>(null); public Task<IReadOnlyList<Session>> GetRecentAsync(int count = 50, CancellationToken cancellationToken = default) => Task.FromResult((IReadOnlyList<Session>)Array.Empty<Session>()); public Task<Session> CreateAsync(string title, string? parentSessionId = null, CancellationToken cancellationToken = default) => Task.FromResult(Session.Create(title)); public Task<Session> UpdateAsync(Session session, CancellationToken cancellationToken = default) => Task.FromResult(session); public Task DeleteAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private class StubMessageService : IMessageService { public Task<Message?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<Message?>(null); public Task<IReadOnlyList<Message>> GetBySessionAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult((IReadOnlyList<Message>)Array.Empty<Message>()); public Task<Message> CreateAsync(Message message, CancellationToken cancellationToken = default) => Task.FromResult(message); public Task<Message> UpdateAsync(Message message, CancellationToken cancellationToken = default) => Task.FromResult(message); public Task DeleteAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask; public Task DeleteBySessionAsync(string sessionId, CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private class StubToolService : IToolService { public IReadOnlyList<ITool> GetTools() => Array.Empty<ITool>(); public ITool? GetTool(string name) => null; public Task<ToolResult> ExecuteAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken = default) => Task.FromResult(new ToolResult { Content = string.Empty }); public IReadOnlyList<LLMTool> GetLLMTools() => Array.Empty<LLMTool>(); }
    #endregion
}
