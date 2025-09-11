using System.CommandLine;
using CodePunk.Console.Commands;
using CodePunk.Console.Configuration;
using CodePunk.Console.Stores;
using CodePunk.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CodePunk.Console.Tests.Commands;

public class PlanCommandTests
{
    private static (RootCommand root, IServiceProvider sp, string tmp) Build()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "codepunk-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", tmp);
        Directory.CreateDirectory(tmp);
        var builder = Host.CreateApplicationBuilder([]);
        builder.Services.AddCodePunkServices(builder.Configuration);
        builder.Services.AddCodePunkConsole();
        var host = builder.Build();
        return (RootCommandFactory.Create(host.Services), host.Services, tmp);
    }

    [Fact]
    public async Task Plan_Create_And_List()
    {
        var (root, sp, tmp) = Build();
        try
        {
            var code = await root.InvokeAsync(["plan", "create", "--goal", "Improve performance"]);
            Assert.Equal(0, code);
            code = await root.InvokeAsync(["plan", "list", "--take", "5"]);
            Assert.Equal(0, code);
            var store = sp.GetRequiredService<IPlanFileStore>();
            var items = await store.ListAsync();
            Assert.Single(items);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public async Task Plan_Show_NotFound_DoesNotThrow()
    {
        var (root, sp, tmp) = Build();
        try
        {
            var code = await root.InvokeAsync(["plan", "show", "--id", "missing"]);
            Assert.Equal(0, code); // handler writes error message but exit code stays 0
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }
}
