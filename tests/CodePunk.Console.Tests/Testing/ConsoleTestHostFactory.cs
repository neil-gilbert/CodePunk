using CodePunk.Console.Configuration;
using CodePunk.Console.Stores;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Chat;
using CodePunk.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace CodePunk.Console.Tests.Testing;

/// <summary>
/// Factory for creating a configured host and exposing underlying mocks for console command tests.
/// </summary>
public static class ConsoleTestHostFactory
{
    public static ConsoleTestHostContext Create(
        Mock<ISessionFileStore>? sessionStoreMock = null,
        Mock<IAgentStore>? agentStoreMock = null,
        Mock<ILLMService>? llmMock = null,
        bool disableDefaultLLM = false)
    {
        sessionStoreMock ??= new Mock<ISessionFileStore>();
        agentStoreMock ??= new Mock<IAgentStore>();
        llmMock ??= new Mock<ILLMService>();

        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
        builder.Services.AddLogging();
        builder.Services.AddCodePunkConsole();

        builder.Services.AddSingleton(sessionStoreMock.Object);
        builder.Services.AddSingleton(agentStoreMock.Object);

        var mockSessionService = new Mock<ISessionService>();
        mockSessionService.Setup(s => s.CreateAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string title, string? parent, CancellationToken _) => Core.Models.Session.Create(title));

        var mockMessageService = new Mock<IMessageService>();
        mockMessageService.Setup(m => m.CreateAsync(It.IsAny<Core.Models.Message>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Core.Models.Message msg, CancellationToken _) => msg);
        mockMessageService.Setup(m => m.GetBySessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Core.Models.Message>());

        if (!disableDefaultLLM)
        {
            llmMock.Setup(l => l.SendMessageStreamAsync(It.IsAny<IList<Core.Models.Message>>(), It.IsAny<CancellationToken>()))
                .Returns((IList<Core.Models.Message> _, CancellationToken __) =>
                {
                    async IAsyncEnumerable<LLMStreamChunk> Stream()
                    {
                        yield return new LLMStreamChunk { Content = "AI", IsComplete = true };
                        await Task.CompletedTask;
                    }
                    return Stream();
                });
        }

        var mockToolService = new Mock<IToolService>();

        builder.Services.AddSingleton(mockSessionService.Object);
        builder.Services.AddSingleton(mockMessageService.Object);
        builder.Services.AddSingleton(llmMock.Object);
        builder.Services.AddSingleton(mockToolService.Object);
        builder.Services.AddSingleton<InteractiveChatSession>();

        var host = builder.Build();
        return new ConsoleTestHostContext(host, sessionStoreMock, agentStoreMock, mockSessionService, mockMessageService, llmMock, mockToolService);
    }
}

/// <summary>
/// Holds the host and mocks used for a console test run.
/// </summary>
public sealed record ConsoleTestHostContext(
    IHost Host,
    Mock<ISessionFileStore> SessionStore,
    Mock<IAgentStore> AgentStore,
    Mock<ISessionService> SessionService,
    Mock<IMessageService> MessageService,
    Mock<ILLMService> LlmService,
    Mock<IToolService> ToolService);
