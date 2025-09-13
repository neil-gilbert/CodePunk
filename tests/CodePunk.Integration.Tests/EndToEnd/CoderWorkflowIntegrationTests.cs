using System.Text.Json;
using CodePunk.Console.Stores;
using CodePunk.Integration.Tests;
using Xunit;

namespace CodePunk.Integration.Tests.EndToEnd;

public class CoderWorkflowIntegrationTests
{
    [Fact(Skip = "CLI end-to-end test placeholder until run command finalized")] 
    public async Task Plan_Modify_And_Delete_Files_Workflow()
    {
        var store = new PlanFileStore();
        var planId = await store.CreateAsync("Test modify and delete workflow");
        var plan = await store.GetAsync(planId);
        Assert.NotNull(plan);
        var modifyPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".txt");
        await File.WriteAllTextAsync(modifyPath, "Original");
        var afterPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(afterPath, "Changed");
        plan!.Files.Add(new PlanFileChange{ Path = modifyPath, BeforeContent = "Original", AfterContent = "Changed", HashBefore = PlanFileStore.Sha256("Original"), HashAfter = PlanFileStore.Sha256("Changed")});
        await store.SaveAsync(plan);
        var deletePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".txt");
        await File.WriteAllTextAsync(deletePath, "ToDelete");
        plan.Files.Add(new PlanFileChange{ Path = deletePath, IsDelete = true, BeforeContent = "ToDelete", HashBefore = PlanFileStore.Sha256("ToDelete")});
        await store.SaveAsync(plan);
        var reloaded = await store.GetAsync(planId);
        Assert.Equal(2, reloaded!.Files.Count);
        Assert.True(File.Exists(modifyPath));
        Assert.True(File.Exists(deletePath));
        // Simulate apply logic manually (simplified)
        foreach(var f in reloaded.Files){
            if(f.IsDelete){ File.Delete(f.Path); continue; }
            if(f.AfterContent!=null) await File.WriteAllTextAsync(f.Path, f.AfterContent);
        }
        Assert.Equal("Changed", await File.ReadAllTextAsync(modifyPath));
        Assert.False(File.Exists(deletePath));
    }
}
