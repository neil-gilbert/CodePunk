using CodePunk.Console.Stores;
using CodePunk.Infrastructure.Settings;
using Xunit;

namespace CodePunk.Console.Tests;

public class AuthFileStoreTests
{
    [Fact]
    public async Task Set_List_Remove_Works()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "codepunk-auth-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", tmp);
        try
        {
            var store = new AuthFileStore();
            await store.SetAsync("Anthropic", "sk-ant-1234567890");
            await store.SetAsync("OpenAI", "sk-open-abcdef");

            var list = (await store.ListAsync()).ToArray();
            Assert.Equal(2, list.Length);
            Assert.Contains(list, p => p.Equals("Anthropic", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(list, p => p.Equals("OpenAI", StringComparison.OrdinalIgnoreCase));

            var loaded = await store.LoadAsync();
            Assert.Equal("sk-ant-1234567890", loaded["Anthropic"]);
            Assert.Equal("sk-open-abcdef", loaded["OpenAI"]);

            await store.RemoveAsync("Anthropic");
            var after = await store.LoadAsync();
            Assert.False(after.ContainsKey("Anthropic"));
            Assert.True(after.ContainsKey("OpenAI"));
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
            Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", null);
        }
    }

    [Fact]
    public async Task Setting_Empty_Values_Throws()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "codepunk-auth-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", tmp);
        try
        {
            var store = new AuthFileStore();
            await Assert.ThrowsAsync<ArgumentException>(() => store.SetAsync("", "abc"));
            await Assert.ThrowsAsync<ArgumentException>(() => store.SetAsync("prov", ""));
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
            Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", null);
        }
    }
}
