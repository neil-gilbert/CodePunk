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
            // Ensure the directory exists before starting
            Directory.CreateDirectory(tmp);
            
            // Construct after env var set so captured base dir points at temp path
            var store = new SessionFileStore();
            var sessionId = await store.CreateAsync("Test Session", "agentA", "modelX");
            Assert.False(string.IsNullOrWhiteSpace(sessionId));
            
            await store.AppendMessageAsync(sessionId, "user", "Hello");
            await store.AppendMessageAsync(sessionId, "assistant", "Hi there");
            
            // Small retry loop in case of very fast file system delay
            SessionRecord? record = null;
            for (int i = 0; i < 3 && record == null; i++)
            {
                record = await store.GetAsync(sessionId);
                if (record == null) await Task.Delay(10);
            }
            Assert.NotNull(record);
            var expectedPath = Path.Combine(tmp, "sessions", sessionId + ".json");
            Assert.True(File.Exists(expectedPath), $"Session file was not written to {expectedPath}");
            Assert.Equal(2, record.Messages.Count);
            
            var list = await store.ListAsync();
            Assert.Contains(list, m => m.Id == sessionId);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
            Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", null);
        }
    }
}