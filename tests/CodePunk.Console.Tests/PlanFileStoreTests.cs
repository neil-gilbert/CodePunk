using CodePunk.Console.Stores;
using Xunit;

namespace CodePunk.Console.Tests;

public class PlanFileStoreTests
{
    [Fact]
    public async Task Create_And_List_Works()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "codepunk-plan-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", tmp);
        var store = new PlanFileStore();
        var id1 = await store.CreateAsync("Add new feature");
        await Task.Delay(20);
        var id2 = await store.CreateAsync("Refactor module");
        var list = await store.ListAsync();
        Assert.Equal(2, list.Count);
        Assert.Contains(list, p => p.Id == id1);
        Assert.Contains(list, p => p.Id == id2);
        var rec = await store.GetAsync(id1);
        Assert.NotNull(rec);
        Assert.Equal("Add new feature", rec!.Definition.Goal);
        try { Directory.Delete(tmp, true); } catch { }
    }
}
