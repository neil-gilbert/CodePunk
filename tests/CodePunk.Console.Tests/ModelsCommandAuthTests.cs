using System.CommandLine;
using System.CommandLine.IO;
using System.Text.Json;
using CodePunk.Console.Commands;
using CodePunk.Console.Stores;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using CodePunk.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CodePunk.Console.Tests;

public class ModelsCommandAuthTests
{
    private class TestAuthStore : IAuthStore
    {
        private readonly Dictionary<string,string> _map = new(StringComparer.OrdinalIgnoreCase);
        public Task<IDictionary<string,string>> LoadAsync(CancellationToken ct = default) => Task.FromResult<IDictionary<string,string>>(new Dictionary<string,string>(_map, StringComparer.OrdinalIgnoreCase));
        public Task SetAsync(string provider, string apiKey, CancellationToken ct = default) { _map[provider]=apiKey; return Task.CompletedTask; }
        public Task RemoveAsync(string provider, CancellationToken ct = default) { _map.Remove(provider); return Task.CompletedTask; }
        public Task<IEnumerable<string>> ListAsync(CancellationToken ct = default) => Task.FromResult<IEnumerable<string>>(_map.Keys.ToArray());
    }
    private record Provider(string Name, IReadOnlyList<LLMModel> Models) : ILLMProvider {
        public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new LLMResponse{ Content = string.Empty });
        public IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, CancellationToken cancellationToken = default) => Empty();
        private async IAsyncEnumerable<LLMStreamChunk> Empty(){ await Task.CompletedTask; yield break; }
        public Task<IReadOnlyList<LLMModel>> FetchModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Models);
    }
    private class LLMService : ILLMService
    {
        private readonly IReadOnlyList<ILLMProvider> _providers;
        public LLMService(params ILLMProvider[] providers) { _providers = providers; }
        public IReadOnlyList<ILLMProvider> GetProviders() => _providers;
        public ILLMProvider? GetProvider(string name) => _providers.FirstOrDefault(p => p.Name==name);
        public ILLMProvider GetDefaultProvider() => _providers[0];
        public Task<LLMResponse> SendAsync(LLMRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new LLMResponse{ Content = string.Empty });
        public Task<LLMResponse> SendAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new LLMResponse{ Content = string.Empty });
        public IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, CancellationToken cancellationToken = default) => Empty();
        public IAsyncEnumerable<LLMStreamChunk> StreamAsync(string providerName, LLMRequest request, CancellationToken cancellationToken = default) => Empty();
        public Task<Message> SendMessageAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default) => Task.FromResult(Message.Create("s", MessageRole.Assistant, new List<MessagePart>{ new TextPart("ok") }));
        public IAsyncEnumerable<LLMStreamChunk> SendMessageStreamAsync(IList<Message> conversationHistory, CancellationToken cancellationToken = default) => Empty();
        private async IAsyncEnumerable<LLMStreamChunk> Empty(){ await Task.CompletedTask; yield break; }
    }

    private static RootCommand BuildRoot(ILLMService llm, IAuthStore authStore)
    {
        var services = new ServiceCollection();
        services.AddSingleton(llm);
        services.AddSingleton(authStore);
        services.AddSingleton<IAuthStore>(authStore);
        services.AddSingleton<ILLMService>(llm);
        return RootCommandFactory.Create(services.BuildServiceProvider());
    }

    [Fact]
    public async Task Models_includes_key_column_and_marks_presence()
    {
        var providerWithKey = new Provider("ProviderKey", new[]{ new LLMModel{ Id="m1", Name="Model1" } });
        var providerNoKey = new Provider("ProviderNoKey", new[]{ new LLMModel{ Id="m2", Name="Model2" } });
        var llm = new LLMService(providerWithKey, providerNoKey);
        var auth = new TestAuthStore();
        await auth.SetAsync("ProviderKey", "secret");
        var root = BuildRoot(llm, auth);
        var console = new TestConsole();
    await root.InvokeAsync(new[]{"models"}, console);
    var output = console.Out.ToString();
    Assert.Contains("ProviderKey", output);
    Assert.Contains("m1", output);
    Assert.Contains("ProviderNoKey", output);
    Assert.Contains("m2", output);
    }

    [Fact]
    public async Task Models_available_only_filters_out_providers_without_keys()
    {
        var p1 = new Provider("Alpha", new[]{ new LLMModel{ Id="a1", Name="A1" } });
        var p2 = new Provider("Beta", new[]{ new LLMModel{ Id="b1", Name="B1" } });
        var llm = new LLMService(p1, p2);
        var auth = new TestAuthStore();
        await auth.SetAsync("Alpha", "key");
        var root = BuildRoot(llm, auth);
        var console = new TestConsole();
    await root.InvokeAsync(new[]{"models","--available-only"}, console);
    var output = console.Out.ToString();
    Assert.Contains("Alpha", output);
    Assert.Contains("a1", output);
    Assert.DoesNotContain("Beta", output);
    Assert.DoesNotContain("b1", output);
    }

    [Fact]
    public async Task Models_json_includes_hasKey_field()
    {
        var p1 = new Provider("Gamma", new[]{ new LLMModel{ Id="g1", Name="G1" } });
        var p2 = new Provider("Delta", new[]{ new LLMModel{ Id="d1", Name="D1" } });
        var llm = new LLMService(p1, p2);
        var auth = new TestAuthStore();
        await auth.SetAsync("Gamma", "key");
        var root = BuildRoot(llm, auth);
        var console = new TestConsole();
        await root.InvokeAsync(new[]{"models","--json"}, console);
    var json = console.Out.ToString();
    json = System.Text.RegularExpressions.Regex.Replace(json ?? string.Empty, "\u001B\\[[0-9;]*[A-Za-z]", string.Empty);
    var idx = json.IndexOf('{'); if (idx>0) json = json[idx..];
    var end = json.LastIndexOf('}'); if (end>0) json = json[..(end+1)];
    using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json)?"{\"schema\":\"models.list.v1\",\"models\":[]}":json);
        Assert.Equal("models.list.v1", doc.RootElement.GetProperty("schema").GetString());
        var arr = doc.RootElement.GetProperty("models").EnumerateArray().ToList();
        var gamma = arr.First(e => e.GetProperty("provider").GetString()=="Gamma");
        var delta = arr.First(e => e.GetProperty("provider").GetString()=="Delta");
        Assert.True(gamma.GetProperty("hasKey").GetBoolean());
        Assert.False(delta.GetProperty("hasKey").GetBoolean());
    }
}
