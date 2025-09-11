using System.CommandLine;
using CodePunk.Console.Commands;
using CodePunk.Console.Configuration;
using CodePunk.Console.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CodePunk.Console.Tests.Commands;

public class AgentCommandTests
{
    [Fact]
    public async Task Agent_Create_List_Show_Delete_Flow()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "codepunk-agent-flow-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", tmp);
        Directory.CreateDirectory(tmp);
        try
        {
            var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
            builder.Services.AddLogging();
            builder.Services.AddCodePunkConsole();
            var host = builder.Build();
            var root = RootCommandFactory.Create(host.Services);

            await root.InvokeAsync(new[]{"agent","create","--name","dev","--provider","Anthropic","--model","claude"});
            var store = host.Services.GetRequiredService<IAgentStore>();
            var list = await store.ListAsync();
            Assert.Contains(list, a => a.Name == "dev" && a.Provider == "Anthropic");
            var fetched = await store.GetAsync("dev");
            Assert.NotNull(fetched);
            await root.InvokeAsync(new[]{"agent","delete","--name","dev"});
            var after = await store.ListAsync();
            Assert.DoesNotContain(after, a => a.Name == "dev");
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
            Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", null);
        }
    }

    [Fact]
    public async Task Agent_Create_Duplicate_WithoutOverwrite_Fails()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "codepunk-agent-flow-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", tmp);
        Directory.CreateDirectory(tmp);
        try
        {
            var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
            builder.Services.AddLogging();
            builder.Services.AddCodePunkConsole();
            var host = builder.Build();
            var root = RootCommandFactory.Create(host.Services);
            var firstCode = await root.InvokeAsync(new[]{"agent","create","--name","dup","--provider","Anthropic"});
            Assert.Equal(0, firstCode);
            var secondCode = await root.InvokeAsync(new[]{"agent","create","--name","dup","--provider","Anthropic"});
            // System.CommandLine swallows thrown exception and sets non-zero exit; verify state unchanged
            Assert.NotEqual(0, secondCode);
            var store = host.Services.GetRequiredService<IAgentStore>();
            var list = await store.ListAsync();
            var dup = list.FirstOrDefault(a => a.Name == "dup");
            Assert.NotNull(dup);
            Assert.Equal("Anthropic", dup!.Provider); // not overwritten
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
            Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", null);
        }
    }

    [Fact]
    public async Task Agent_Create_WithOverwrite_Succeeds()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "codepunk-agent-flow-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", tmp);
        Directory.CreateDirectory(tmp);
        try
        {
            var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
            builder.Services.AddLogging();
            builder.Services.AddCodePunkConsole();
            var host = builder.Build();
            var root = RootCommandFactory.Create(host.Services);
            await root.InvokeAsync(new[]{"agent","create","--name","dev","--provider","Anthropic"});
            await root.InvokeAsync(new[]{"agent","create","--name","dev","--provider","OpenAI","--overwrite"});
            var store = host.Services.GetRequiredService<IAgentStore>();
            var updated = await store.GetAsync("dev");
            Assert.NotNull(updated);
            Assert.Equal("OpenAI", updated!.Provider);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
            Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", null);
        }
    }
}
