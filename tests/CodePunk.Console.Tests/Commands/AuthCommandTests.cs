using System.CommandLine;
using CodePunk.Console.Commands;
using CodePunk.Console.Stores;
using CodePunk.Console.Tests.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CodePunk.Console.Tests.Commands;

public class AuthCommandTests
{
    [Fact]
    public async Task Auth_Login_List_Logout_Flow_WritesExpectedOutput()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "codepunk-auth-flow-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", tmp);
        Directory.CreateDirectory(tmp);
        try
        {
            var hostCtx = ConsoleTestHostFactory.Create();
            var root = RootCommandFactory.Create(hostCtx.Host.Services);
            await root.InvokeAsync(new[]{"auth","login","--provider","Test","--key","abc123"});
            await root.InvokeAsync(new[]{"auth","login","--provider","Other","--key","xyz789"});
            await root.InvokeAsync(new[]{"auth","list"});
            var store = hostCtx.Host.Services.GetRequiredService<IAuthStore>();
            var loaded = await store.LoadAsync();
            Assert.Equal(2, loaded.Count);
            Assert.True(loaded.ContainsKey("Test"));
            Assert.Equal("abc123", loaded["Test"]);
            await root.InvokeAsync(new[]{"auth","logout","--provider","Test"});
            var after = await store.LoadAsync();
            Assert.False(after.ContainsKey("Test"));
            Assert.True(after.ContainsKey("Other"));
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
            Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", null);
        }
    }
}
