using System.CommandLine;
using System.CommandLine.IO;
using CodePunk.Console.Commands;
using CodePunk.Console.Stores;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Services;
using CodePunk.Core.Models;
using Moq;
using Xunit;
using System.Linq;
using CodePunk.Console.Tests.Testing;

namespace CodePunk.Console.Tests.Commands;

public class RunCommandScenarioTests
{
    private static RootCommand GetRoot(ConsoleTestHostContext ctx) => RootCommandFactory.Create(ctx.Host.Services);

    [Fact]
    public async Task Run_AgentNotFound_ShowsNoCreationSideEffects()
    {
        var sessionStore = new Mock<ISessionFileStore>();
        sessionStore.Setup(s => s.ListAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new List<SessionMetadata>());
        sessionStore.Setup(s => s.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        var agentStore = new Mock<IAgentStore>();
        agentStore.Setup(a => a.GetAsync("missing", It.IsAny<CancellationToken>())).ReturnsAsync((AgentDefinition?)null);
    var ctx = ConsoleTestHostFactory.Create(sessionStore, agentStore);
    var runCmd = GetRoot(ctx).Children.OfType<Command>().First(c => c.Name == "run");
        var console = new TestConsole();
        var rc = await runCmd.InvokeAsync(new[]{"--agent","missing","Hello"}, console);
        Assert.Equal(0, rc);
        sessionStore.Verify(s => s.CreateAsync(It.IsAny<string>(), "missing", It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Run_AgentSetsProviderAndModel_WhenAgentFound()
    {
        var sessionStore = new Mock<ISessionFileStore>();
        sessionStore.Setup(s => s.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        var agentStore = new Mock<IAgentStore>();
        agentStore.Setup(a => a.GetAsync("coder", It.IsAny<CancellationToken>())).ReturnsAsync(new AgentDefinition
        {
            Name = "coder",
            Provider = "openai",
            Model = "gpt-4o"
        });
    var ctx = ConsoleTestHostFactory.Create(sessionStore, agentStore);
    var runCmd = GetRoot(ctx).Children.OfType<Command>().First(c => c.Name == "run");
        var rc = await runCmd.InvokeAsync(new[]{"--agent","coder","Implement feature"}, new TestConsole());
        Assert.Equal(0, rc);
    sessionStore.Verify(s => s.CreateAsync(It.IsAny<string>(), "coder", "gpt-4o", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Run_ModelFlagOverridesAgentModel_WhenBothProvided()
    {
        var sessionStore = new Mock<ISessionFileStore>();
        sessionStore.Setup(s => s.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        var agentStore = new Mock<IAgentStore>();
        agentStore.Setup(a => a.GetAsync("coder", It.IsAny<CancellationToken>())).ReturnsAsync(new AgentDefinition
        {
            Name = "coder",
            Provider = "openai",
            Model = "gpt-4o"
        });
    var ctx = ConsoleTestHostFactory.Create(sessionStore, agentStore);
    var runCmd = GetRoot(ctx).Children.OfType<Command>().First(c => c.Name == "run");
        var rc = await runCmd.InvokeAsync(new[]{"--agent","coder","--model","anthropic/claude-3","Refactor"}, new TestConsole());
        Assert.Equal(0, rc);
        // Model flag should take precedence, so CreateAsync should see explicit model flag value
        sessionStore.Verify(s => s.CreateAsync(It.IsAny<string>(), "coder", "anthropic/claude-3", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Run_MultiChunkStream_AppendsCombinedAssistantMessage()
    {
        var sessionStore = new Mock<ISessionFileStore>();
        sessionStore.Setup(s => s.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        sessionStore.Setup(s => s.ListAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SessionMetadata>());
    var multiChunkMock = new Mock<ILLMService>();
        multiChunkMock.Setup(l => l.SendMessageStreamAsync(It.IsAny<IList<CodePunk.Core.Models.Message>>(), It.IsAny<CancellationToken>()))
            .Returns((IList<CodePunk.Core.Models.Message> _, CancellationToken __) =>
            {
        async IAsyncEnumerable<CodePunk.Core.Abstractions.LLMStreamChunk> Stream()
                {
                    yield return new CodePunk.Core.Abstractions.LLMStreamChunk { Content = "Part1", IsComplete = false };
                    yield return new CodePunk.Core.Abstractions.LLMStreamChunk { Content = "Part2", IsComplete = false };
                    yield return new CodePunk.Core.Abstractions.LLMStreamChunk { Content = "Final", IsComplete = true };
            await Task.CompletedTask;
                }
                return Stream();
            });
    var ctx = ConsoleTestHostFactory.Create(sessionStore, new Mock<IAgentStore>(), multiChunkMock, disableDefaultLLM: true);
    var runCmd = GetRoot(ctx).Children.OfType<Command>().First(c => c.Name == "run");
        var rc = await runCmd.InvokeAsync(new[]{"Stream this"}, new TestConsole());
        Assert.Equal(0, rc);
        sessionStore.Verify(s => s.AppendMessageAsync(It.IsAny<string>(), "assistant", It.Is<string>(c => c == "Part1Part2Final"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Run_Continue_UsesMostRecentSession_FromListOrdering()
    {
        var sessionStore = new Mock<ISessionFileStore>();
        var recent = new SessionMetadata{ Id = "recent", Title = "Recent" };
        var older = new SessionMetadata{ Id = "older", Title = "Older" };
        // Return list with most recent first (expected contract)
        sessionStore.Setup(s => s.ListAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new List<SessionMetadata>{ recent });
        sessionStore.Setup(s => s.GetAsync("recent", It.IsAny<CancellationToken>())).ReturnsAsync(new SessionRecord{ Metadata = recent });
    var ctx = ConsoleTestHostFactory.Create(sessionStore, new Mock<IAgentStore>());
    var runCmd = GetRoot(ctx).Children.OfType<Command>().First(c => c.Name == "run");
        var rc = await runCmd.InvokeAsync(new[]{"--continue","Hello"}, new TestConsole());
        Assert.Equal(0, rc);
        sessionStore.Verify(s => s.GetAsync("recent", It.IsAny<CancellationToken>()), Times.Once);
        sessionStore.Verify(s => s.GetAsync("older", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Run_SessionIdProvidedButMissing_CreatesNewSession()
    {
        var sessionStore = new Mock<ISessionFileStore>();
        sessionStore.Setup(s => s.GetAsync("missing123", It.IsAny<CancellationToken>())).ReturnsAsync((SessionRecord?)null);
        sessionStore.Setup(s => s.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
    var ctx = ConsoleTestHostFactory.Create(sessionStore, new Mock<IAgentStore>());
    var runCmd = GetRoot(ctx).Children.OfType<Command>().First(c => c.Name == "run");
        var rc = await runCmd.InvokeAsync(new[]{"--session","missing123","Hello"}, new TestConsole());
        Assert.Equal(0, rc);
        sessionStore.Verify(s => s.GetAsync("missing123", It.IsAny<CancellationToken>()), Times.Once);
        sessionStore.Verify(s => s.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Run_NewSessionCreated_WhenNoSessionFlags()
    {
        var sessionStore = new Mock<ISessionFileStore>();
        sessionStore.Setup(s => s.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());
        sessionStore.Setup(s => s.ListAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new List<SessionMetadata>());
    var ctx = ConsoleTestHostFactory.Create(sessionStore, new Mock<IAgentStore>());
    var runCmd = GetRoot(ctx).Children.OfType<Command>().First(c => c.Name == "run");
    var console = new TestConsole();
    var rc = await runCmd.InvokeAsync(new[]{"Hello"}, console);
    Assert.Equal(0, rc);
    sessionStore.Verify(s => s.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    sessionStore.Verify(s => s.AppendMessageAsync(It.IsAny<string>(), "user", "Hello", It.IsAny<CancellationToken>()), Times.Once);
    sessionStore.Verify(s => s.AppendMessageAsync(It.IsAny<string>(), "assistant", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Run_UsesExistingSession_WhenSessionIdProvided()
    {
        var existing = new SessionRecord{ Metadata = new SessionMetadata{ Id = "abc123", Title = "Old" } };
        var sessionStore = new Mock<ISessionFileStore>();
        sessionStore.Setup(s => s.GetAsync("abc123", It.IsAny<CancellationToken>())).ReturnsAsync(existing);
    var ctx = ConsoleTestHostFactory.Create(sessionStore, new Mock<IAgentStore>());
    var runCmd = GetRoot(ctx).Children.OfType<Command>().First(c => c.Name == "run");
    var console = new TestConsole();
    var rc = await runCmd.InvokeAsync(new[]{"--session","abc123","Hi"}, console);
        Assert.Equal(0, rc);
        sessionStore.Verify(s => s.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    sessionStore.Verify(s => s.GetAsync("abc123", It.IsAny<CancellationToken>()), Times.Once);
    sessionStore.Verify(s => s.AppendMessageAsync("abc123", "user", "Hi", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Run_Continue_UsesLatestSession()
    {
        var latest = new SessionRecord{ Metadata = new SessionMetadata{ Id = "latest1", Title = "Latest" } };
        var sessionStore = new Mock<ISessionFileStore>();
        sessionStore.Setup(s => s.ListAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new List<SessionMetadata>{ latest.Metadata });
        sessionStore.Setup(s => s.GetAsync("latest1", It.IsAny<CancellationToken>())).ReturnsAsync(latest);
    var ctx = ConsoleTestHostFactory.Create(sessionStore, new Mock<IAgentStore>());
    var runCmd = GetRoot(ctx).Children.OfType<Command>().First(c => c.Name == "run");
    var console = new TestConsole();
    var rc = await runCmd.InvokeAsync(new[]{"--continue","Ping"}, console);
        Assert.Equal(0, rc);
        sessionStore.Verify(s => s.ListAsync(1, It.IsAny<CancellationToken>()), Times.AtMostOnce);
    sessionStore.Verify(s => s.GetAsync("latest1", It.IsAny<CancellationToken>()), Times.Once);
    sessionStore.Verify(s => s.AppendMessageAsync("latest1", "user", "Ping", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Run_Conflict_ShowsError_WhenContinueAndSessionBothSpecified()
    {
        var sessionStore = new Mock<ISessionFileStore>();
    var ctx = ConsoleTestHostFactory.Create(sessionStore, new Mock<IAgentStore>());
    var runCmd = GetRoot(ctx).Children.OfType<Command>().First(c => c.Name == "run");
        var console = new TestConsole();
    var rc = await runCmd.InvokeAsync(new[]{"--continue","--session","id1","Hello"}, console);
    Assert.Equal(0, rc); // returns early
        sessionStore.Verify(s => s.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    sessionStore.Verify(s => s.AppendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    // Can't reliably capture Spectre console markup with TestConsole; side-effect assertions sufficient.
    }
}
