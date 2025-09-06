using CodePunk.Console.Stores;
using Xunit;

namespace CodePunk.Console.Tests;

public class AgentFileStoreTests
{
    [Fact]
    public async Task Create_List_Get_Delete_Works()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "codepunk-agent-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", tmp);
        try
        {
            var store = new AgentFileStore();
            var def = new AgentDefinition { Name = "dev", Provider = "Anthropic", Model = "claude-3-5-sonnet" };
            await store.CreateAsync(def, overwrite: false);
            var list = await store.ListAsync();
            Assert.Single(list);
            var fetched = await store.GetAsync("dev");
            Assert.NotNull(fetched);
            Assert.Equal("Anthropic", fetched!.Provider);
            await store.DeleteAsync("dev");
            var afterDelete = await store.ListAsync();
            Assert.Empty(afterDelete);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
            Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", null);
        }
    }
}