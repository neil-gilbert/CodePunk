using System.CommandLine;
using CodePunk.Console.Commands;
using CodePunk.Console.Stores;
using CodePunk.Console.Tests.Testing;
using Moq;
using Xunit;

namespace CodePunk.Console.Tests.Commands;

public class RootCommandFactoryTitleTests
{
    private static (Command run, Mock<ISessionFileStore> store) Build()
    {
        var sessionStore = new Mock<ISessionFileStore>();
        sessionStore.Setup(s => s.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("id1");
        var ctx = ConsoleTestHostFactory.Create(sessionStoreMock: sessionStore);
        var root = RootCommandFactory.Create(ctx.Host.Services);
        var run = root.Children.OfType<Command>().First(c => c.Name == "run");
        return (run, sessionStore);
    }

    [Fact]
    public async Task RunCommand_TrimsLongMessageTitle()
    {
    var (run, store) = Build();
        var longMsg = new string('x', 120);
        await run.InvokeAsync(new[]{ longMsg });
        store.Verify(s => s.CreateAsync(It.Is<string>(t => t.Length <= 80), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
