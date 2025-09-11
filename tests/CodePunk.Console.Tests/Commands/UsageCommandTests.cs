using CodePunk.Console.Commands;
using CodePunk.Core.Chat;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Services;
using Moq;
using Xunit;
using Microsoft.Extensions.Logging;

namespace CodePunk.Console.Tests.Commands;

public class UsageCommandTests
{
    private InteractiveChatSession BuildSession()
    {
        var sessionService = new Mock<ISessionService>();
        var messageService = new Mock<IMessageService>();
        var llmService = new Mock<ILLMService>();
        var toolService = new Mock<IToolService>();
        var logger = new Mock<ILogger<InteractiveChatSession>>();
    var s = new InteractiveChatSession(sessionService.Object, messageService.Object, llmService.Object, toolService.Object, logger.Object, new ChatSessionEventStream());
        // Fake an active session
        var session = CodePunk.Core.Models.Session.Create("Test");
        s.GetType().GetProperty("CurrentSession")!.SetValue(s, session);
        // Simulate previous accumulation
        s.GetType().GetProperty("AccumulatedPromptTokens")!.SetValue(s, 100L);
        s.GetType().GetProperty("AccumulatedCompletionTokens")!.SetValue(s, 250L);
        s.GetType().GetProperty("AccumulatedCost")!.SetValue(s, 0.1234m);
        return s;
    }

    [Fact]
    public async Task UsageCommand_ShouldReturnOk_WhenSessionActive()
    {
        var session = BuildSession();
        var cmd = new UsageCommand(session);
        var result = await cmd.ExecuteAsync(Array.Empty<string>());
        Assert.True(result.Success);
    }

    [Fact]
    public async Task UsageCommand_ShouldFail_WhenNoSession()
    {
        var sessionService = new Mock<ISessionService>();
        var messageService = new Mock<IMessageService>();
        var llmService = new Mock<ILLMService>();
        var toolService = new Mock<IToolService>();
        var logger = new Mock<ILogger<InteractiveChatSession>>();
    var s = new InteractiveChatSession(sessionService.Object, messageService.Object, llmService.Object, toolService.Object, logger.Object, new ChatSessionEventStream());
        var cmd = new UsageCommand(s);
        var result = await cmd.ExecuteAsync(Array.Empty<string>());
        Assert.False(result.Success);
    }
}
