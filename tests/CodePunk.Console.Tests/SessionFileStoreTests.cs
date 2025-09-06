using CodePunk.Console.Stores;
using Xunit;

namespace CodePunk.Console.Tests;

public class SessionFileStoreTests
{
    [Fact]
    public async Task Create_And_Append_Works()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "codepunk-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", tmp);
        try
        {
            var store = new SessionFileStore();
            var sessionId = await store.CreateAsync("Test Session", "agentA", "modelX");
            Assert.False(string.IsNullOrWhiteSpace(sessionId));
            await store.AppendMessageAsync(sessionId, "user", "Hello");
            await store.AppendMessageAsync(sessionId, "assistant", "Hi there");
            var record = await store.GetAsync(sessionId);
            Assert.True(File.Exists(Path.Combine(tmp, "sessions", sessionId + ".json")), "Session file was not written");
            Assert.NotNull(record); // record should deserialize
            Assert.Equal(2, record!.Messages.Count);
            var list = await store.ListAsync();
            Assert.Single(list);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
            Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", null);
        }
    }
}