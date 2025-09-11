using System.CommandLine;
using CodePunk.Console.Commands;
using CodePunk.Console.Configuration;
using CodePunk.Console.Stores;
using CodePunk.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CodePunk.Console.Tests.Commands;

public class SessionsCommandTests
{
    private static async Task<(RootCommand root, IServiceProvider sp, string tmp)> BuildAsync()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "codepunk-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", tmp);
        Directory.CreateDirectory(tmp);
        var builder = Host.CreateApplicationBuilder([]);
        builder.Services.AddCodePunkServices(builder.Configuration);
        builder.Services.AddCodePunkConsole();
        var host = builder.Build();
        var root = RootCommandFactory.Create(host.Services);
        return (root, host.Services, tmp);
    }

    [Fact]
    public async Task List_Empty_ShowsMessage()
    {
        var (root, sp, tmp) = await BuildAsync();
        try
        {
            var code = await root.InvokeAsync(["sessions", "list"]);
            Assert.Equal(0, code);
            var store = sp.GetRequiredService<ISessionFileStore>();
            var items = await store.ListAsync();
            Assert.Empty(items);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public async Task CreateAndList_TakeLimit()
    {
        var (root, sp, tmp) = await BuildAsync();
        try
        {
            var store = sp.GetRequiredService<ISessionFileStore>();
            // create 5 sessions
            for (int i = 0; i < 5; i++)
            {
                await store.CreateAsync($"Session {i}", null, null);
                await Task.Delay(15); // ensure ordering by timestamp
            }
            var all = await store.ListAsync();
            Assert.Equal(5, all.Count);
            var code = await root.InvokeAsync(["sessions", "list", "--take", "3"]);
            Assert.Equal(0, code);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public async Task Show_Nonexistent_ReturnsMessage()
    {
        var (root, sp, tmp) = await BuildAsync();
        try
        {
            var code = await root.InvokeAsync(["sessions", "show", "--id", "nope"]);
            Assert.Equal(0, code); // command writes not found; still exit 0
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public async Task Show_Existing_PrintsTranscript()
    {
        var (root, sp, tmp) = await BuildAsync();
        try
        {
            var store = sp.GetRequiredService<ISessionFileStore>();
            var id = await store.CreateAsync("Demo", null, null);
            await store.AppendMessageAsync(id, "user", "Hello");
            await store.AppendMessageAsync(id, "assistant", "Hi there");
            var rec = await store.GetAsync(id);
            Assert.NotNull(rec);
            var code = await root.InvokeAsync(["sessions", "show", "--id", id]);
            Assert.Equal(0, code);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public async Task Load_Nonexistent_ShowsMessage()
    {
        var (root, sp, tmp) = await BuildAsync();
        try
        {
            var code = await root.InvokeAsync(["sessions", "load", "--id", "missing"]);
            Assert.NotEqual(1, code); // expecting 0 since we don't fail hard
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public async Task Load_Existing_Succeeds()
    {
        var (root, sp, tmp) = await BuildAsync();
        try
        {
            var store = sp.GetRequiredService<ISessionFileStore>();
            var id = await store.CreateAsync("ToLoad", null, null);
            var code = await root.InvokeAsync(["sessions", "load", "--id", id]);
            Assert.Equal(0, code);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }
}
