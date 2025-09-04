using System.Text.Json;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Chat;
using CodePunk.Core.Models;
using CodePunk.Core.Services;
using CodePunk.Core.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CodePunk.Integration.Tests.EndToEnd;

public class BasicToolExecutionTests : IntegrationTestBase
{
    
    [Fact(Skip = "Temporarily ignored per request")] 
    public async Task InteractiveChatSession_WithAnthropic_Should_WriteFileToDisk()
    {
        // Arrange
        var sessionService = ServiceProvider.GetRequiredService<ISessionService>();
        var messageService = ServiceProvider.GetRequiredService<IMessageService>();
        var llmService = ServiceProvider.GetRequiredService<ILLMService>();
        var toolService = ServiceProvider.GetRequiredService<IToolService>();
        var logger = ServiceProvider.GetRequiredService<ILogger<InteractiveChatSession>>();

        var chatSession = new InteractiveChatSession(sessionService, messageService, llmService, toolService, logger);
        await chatSession.StartNewSessionAsync("Test file creation session");

        var filePath = $"{Guid.NewGuid()}.txt";
        var fileContent = "Hello from the integration test!";
        var prompt = $"Please create a file named '{filePath}' with the content: {fileContent}";

        // Act
        var finalMessage = await chatSession.SendMessageAsync(prompt);

        // Assert
        Assert.NotNull(finalMessage);
        
        // Verify file was created
        Assert.True(File.Exists(filePath), $"File '{filePath}' should have been created.");
        var actualContent = await File.ReadAllTextAsync(filePath);
        Assert.Equal(fileContent, actualContent);

        // Cleanup
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
