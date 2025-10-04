using System.Text.Json;
using CodePunk.ComponentTests.TestHelpers;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Chat;
using CodePunk.Core.Models;
using CodePunk.Core.Services;
using CodePunk.Core.Tools;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CodePunk.ComponentTests;

/// <summary>
/// End-to-end behavioral tests that verify complete user workflows from request to outcome
/// These test at the outermost boundaries: user sends message → AI responds → file system changes
/// </summary>
public class UserBehaviorTests : WorkspaceTestBase
{
    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<ILLMService> _mockLLMService;
    private readonly List<Message> _capturedLLMMessages = new();
    private int _llmCallCount = 0;

    public UserBehaviorTests() : base("user_behavior")
    {
        _mockLLMService = new Mock<ILLMService>();
        SetupLLMServiceCapture();

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task User_RequestsFileCreation_AI_CallsWriteTool_FileIsCreated()
    {
        // Arrange: Setup AI to respond with write_file tool call
        var fileName = "hello.txt";
        var fileContent = "Hello, World!";

        SetupAIToCallWriteFileTool(fileName, fileContent);
        var chatSession = _serviceProvider.GetRequiredService<InteractiveChatSession>();
        var approvalService = GetTestApprovalService();
        approvalService.AutoApprove = true;

        // Act: User sends natural language request
        await chatSession.StartNewSessionAsync("TestSession");
        var response = await chatSession.SendMessageAsync("Create a hello.txt file with 'Hello, World!' content");

        // Assert: Verify the complete workflow
        // 1. File was actually created on disk
        File.Exists(fileName).Should().BeTrue();
        var actualContent = await File.ReadAllTextAsync(fileName);
        actualContent.Should().Be(fileContent);

        // 2. AI was called with user's request
        _capturedLLMMessages.Should().ContainSingle(m =>
            m.Role == MessageRole.User &&
            m.Parts.OfType<TextPart>().Any(p => p.Content.Contains("Create a hello.txt file")));

        // 3. Response indicates success
        var textParts = response.Parts.OfType<TextPart>();
        textParts.Should().Contain(p => p.Content.Contains("successfully"));
    }

    [Fact]
    public async Task User_RequestsFileCreation_UserCancelsApproval_NoFileCreated_AIInformed()
    {
        // Arrange: Setup AI to call write tool, but user will cancel
        var fileName = "cancelled.txt";
        var fileContent = "This should not be written";

        SetupAIToCallWriteFileTool(fileName, fileContent);
        var chatSession = _serviceProvider.GetRequiredService<InteractiveChatSession>();
        var approvalService = GetTestApprovalService();
        approvalService.CancelAll = true;

        // Act: User requests file creation but cancels when prompted
        await chatSession.StartNewSessionAsync("TestSession");
        var response = await chatSession.SendMessageAsync("Create a cancelled.txt file");

        // Assert: Verify cancellation behavior
        // 1. No file created on disk
        File.Exists(fileName).Should().BeFalse();

        // 2. User received cancellation message
        var textParts = response.Parts.OfType<TextPart>();
        textParts.Should().Contain(p => p.Content.Contains("cancelled"));

        // 3. AI was initially called with user request
        _capturedLLMMessages.Should().ContainSingle(m =>
            m.Role == MessageRole.User &&
            m.Parts.OfType<TextPart>().Any(p => p.Content.Contains("Create a cancelled.txt")));
    }

    [Fact]
    public async Task User_RequestsFileModification_AI_CallsReplaceInFileTool_FileIsModified()
    {
        // Arrange: Create existing file and setup AI to modify it
        var fileName = "existing.txt";
        var originalContent = "Original content\nLine 2\nLine 3";
        await File.WriteAllTextAsync(fileName, originalContent);

        SetupAIToCallReplaceInFileTool(fileName, "Original content", "Modified content");
        var chatSession = _serviceProvider.GetRequiredService<InteractiveChatSession>();
        var approvalService = GetTestApprovalService();
        approvalService.AutoApprove = true;

        // Act: User requests file modification
        await chatSession.StartNewSessionAsync("TestSession");
        var response = await chatSession.SendMessageAsync("Change 'Original content' to 'Modified content' in existing.txt");

        // Assert: Verify complete modification workflow
        // 1. File was actually modified on disk
        var actualContent = await File.ReadAllTextAsync(fileName);
        actualContent.Should().Contain("Modified content");
        actualContent.Should().NotContain("Original content");

        // 2. AI received the modification request
        _capturedLLMMessages.Should().ContainSingle(m =>
            m.Role == MessageRole.User &&
            m.Parts.OfType<TextPart>().Any(p => p.Content.Contains("Change 'Original content'")));

        // 3. Response indicates successful modification
        var textParts = response.Parts.OfType<TextPart>();
        textParts.Should().Contain(p => p.Content.Contains("successfully"));
    }

    [Fact]
    public async Task User_RequestsInvalidFileOperation_AI_CallsTool_UserGetsErrorMessage_NoFileChanges()
    {
        // Arrange: Setup AI to try writing outside workspace
        var invalidPath = "../outside_workspace.txt";
        SetupAIToCallWriteFileTool(invalidPath, "bad content");

        var chatSession = _serviceProvider.GetRequiredService<InteractiveChatSession>();

        // Act: User requests invalid operation
        await chatSession.StartNewSessionAsync("TestSession");
        var response = await chatSession.SendMessageAsync("Write file outside the current directory");

        // Assert: Verify error handling workflow
        // 1. No file created outside workspace
        File.Exists(invalidPath).Should().BeFalse();

        // 2. User received error message
        var textParts = response.Parts.OfType<TextPart>();
        textParts.Should().Contain(p =>
            p.Content.Contains("outside workspace") || p.Content.Contains("Error"));

        // 3. AI was called with user request
        _capturedLLMMessages.Should().ContainSingle(m =>
            m.Role == MessageRole.User &&
            m.Parts.OfType<TextPart>().Any(p => p.Content.Contains("Write file outside")));
    }

    [Fact]
    public async Task User_RequestsMultipleFiles_AI_CallsMultipleTools_AllFilesCreated()
    {
        // Arrange: Setup AI to create multiple files
        var fileNames = new[] { "file1.txt", "file2.txt", "file3.txt" };
        SetupAIToCallMultipleWriteFileTools(fileNames);

        var chatSession = _serviceProvider.GetRequiredService<InteractiveChatSession>();
        var approvalService = GetTestApprovalService();
        approvalService.AutoApprove = true;

        // Act: User requests multiple file creation
        await chatSession.StartNewSessionAsync("TestSession");
        var response = await chatSession.SendMessageAsync("Create three files: file1.txt, file2.txt, and file3.txt");

        // Assert: Verify multiple file workflow
        // 1. All files created on disk
        foreach (var fileName in fileNames)
        {
            File.Exists(fileName).Should().BeTrue($"File {fileName} should have been created");
        }

        // 2. AI received the multi-file request
        _capturedLLMMessages.Should().ContainSingle(m =>
            m.Role == MessageRole.User &&
            m.Parts.OfType<TextPart>().Any(p => p.Content.Contains("Create three files")));

        // 3. Response indicates success
        var textParts = response.Parts.OfType<TextPart>();
        textParts.Should().Contain(p => p.Content.Contains("successfully"));
    }

    private void ConfigureServices(ServiceCollection services)
    {
        // Register complete chat session infrastructure
        services.AddScoped<InteractiveChatSession>();

        // Real services for file operations
        services.AddScoped<IFileEditService, FileEditService>();
        services.AddScoped<IDiffService, DiffService>();
        services.AddScoped<IToolService, ToolService>();

        // Register tools
        services.AddScoped<ITool, ReadFileTool>();
        services.AddScoped<ITool, WriteFileTool>();
        services.AddScoped<ITool, ReplaceInFileTool>();
        services.AddScoped<ITool, ShellTool>();
        services.AddScoped<ITool, ListDirectoryTool>();
        services.AddScoped<ITool, GlobTool>();
        services.AddScoped<ITool, SearchFilesTool>();
        services.AddScoped<ITool, ReadManyFilesTool>();

        // Test doubles for user interactions
        services.AddScoped<TestApprovalService>();
        services.AddScoped<IApprovalService>(provider => provider.GetRequiredService<TestApprovalService>());

        // Mock services for external dependencies
        services.AddScoped<ISessionService, MockSessionService>();
        services.AddScoped<IMessageService, MockMessageService>();
        services.AddSingleton(_mockLLMService.Object);

        // Required loggers
        services.AddSingleton<ILogger<InteractiveChatSession>>(provider => NullLogger<InteractiveChatSession>.Instance);
        services.AddSingleton<ILogger<FileEditService>>(provider => NullLogger<FileEditService>.Instance);
        services.AddSingleton<ILogger<DiffService>>(provider => NullLogger<DiffService>.Instance);
        services.AddSingleton<ILogger<ToolService>>(provider => NullLogger<ToolService>.Instance);

        // Chat session dependencies
        services.AddSingleton<IChatSessionEventStream, ChatSessionEventStream>();
        services.AddSingleton<IChatSessionOptions, ChatSessionOptions>();

        // Configure shell command options
        services.Configure<CodePunk.Core.Configuration.ShellCommandOptions>(options =>
        {
            options.AllowedCommands = new List<string> { "*" };
            options.EnableCommandValidation = false;
        });
    }

    private void SetupLLMServiceCapture()
    {
        _mockLLMService.Setup(x => x.SendMessageAsync(It.IsAny<IList<Message>>(), It.IsAny<CancellationToken>()))
            .Callback<IList<Message>, CancellationToken>((messages, _) =>
            {
                // Only capture messages that aren't already captured
                foreach (var msg in messages)
                {
                    if (!_capturedLLMMessages.Contains(msg))
                    {
                        _capturedLLMMessages.Add(msg);
                    }
                }
            })
            .Returns<IList<Message>, CancellationToken>((messages, _) =>
            {
                // Return the last AI response that was set up
                return Task.FromResult(_currentAIResponse);
            });

        _mockLLMService.Setup(x => x.SendMessageStreamAsync(It.IsAny<IList<Message>>(), It.IsAny<CancellationToken>()))
            .Callback<IList<Message>, CancellationToken>((messages, _) =>
            {
                // Only capture messages that aren't already captured
                foreach (var msg in messages)
                {
                    if (!_capturedLLMMessages.Contains(msg))
                    {
                        _capturedLLMMessages.Add(msg);
                    }
                }
            })
            .Returns<IList<Message>, CancellationToken>((messages, _) =>
            {
                _llmCallCount++;
                // First call: return the configured response (likely with tool calls)
                // Subsequent calls: check if there were tool errors and respond accordingly
                if (_llmCallCount == 1)
                {
                    return ConvertMessageToStream(_currentAIResponse);
                }
                else
                {
                    // Check if last message contains tool errors
                    var lastMessage = messages.LastOrDefault();
                    var hasError = lastMessage?.Parts.OfType<ToolResultPart>().Any(p => p.IsError) ?? false;

                    var responseText = hasError
                        ? "Error: The operation failed due to an invalid path or permission issue"
                        : "Operation completed successfully";

                    var finalMessage = Message.Create("test", MessageRole.Assistant,
                        new[] { new TextPart(responseText) });
                    return ConvertMessageToStream(finalMessage);
                }
            });
    }

    private async IAsyncEnumerable<LLMStreamChunk> ConvertMessageToStream(Message message)
    {
        await Task.Yield(); // Make it actually async

        foreach (var part in message.Parts)
        {
            if (part is TextPart textPart)
            {
                yield return new LLMStreamChunk
                {
                    Content = textPart.Content,
                    IsComplete = false
                };
            }
            else if (part is ToolCallPart toolCallPart)
            {
                yield return new LLMStreamChunk
                {
                    ToolCall = new ToolCall
                    {
                        Id = toolCallPart.Id,
                        Name = toolCallPart.Name,
                        Arguments = toolCallPart.Arguments
                    },
                    IsComplete = false
                };
            }
        }

        yield return new LLMStreamChunk
        {
            IsComplete = true,
            FinishReason = LLMFinishReason.Stop
        };
    }

    private Message _currentAIResponse = Message.Create("test", MessageRole.Assistant, new[] { new TextPart("Default response") });

    private void SetupAIToCallWriteFileTool(string fileName, string content)
    {
        _llmCallCount = 0; // Reset counter for each test
        var toolCallPart = new ToolCallPart(
            Id: "call_1",
            Name: "write_file",
            Arguments: JsonDocument.Parse($@"{{
                ""file_path"": ""{fileName}"",
                ""content"": ""{content}"",
                ""require_approval"": true
            }}").RootElement
        );

        _currentAIResponse = Message.Create("test", MessageRole.Assistant, new MessagePart[] { toolCallPart });
    }

    private void SetupAIToCallReplaceInFileTool(string fileName, string oldString, string newString)
    {
        _llmCallCount = 0; // Reset counter for each test
        var toolCallPart = new ToolCallPart(
            Id: "call_1",
            Name: "replace_in_file",
            Arguments: JsonDocument.Parse($@"{{
                ""file_path"": ""{fileName}"",
                ""old_string"": ""{oldString}"",
                ""new_string"": ""{newString}"",
                ""require_approval"": true
            }}").RootElement
        );

        _currentAIResponse = Message.Create("test", MessageRole.Assistant, new MessagePart[] { toolCallPart });
    }

    private void SetupAIToCallMultipleWriteFileTools(string[] fileNames)
    {
        _llmCallCount = 0; // Reset counter for each test
        var toolCallParts = fileNames.Select((fileName, index) => new ToolCallPart(
            Id: $"call_{index + 1}",
            Name: "write_file",
            Arguments: JsonDocument.Parse($@"{{
                ""file_path"": ""{fileName}"",
                ""content"": ""Content for {fileName}"",
                ""require_approval"": true
            }}").RootElement
        )).ToArray();

        _currentAIResponse = Message.Create("test", MessageRole.Assistant, toolCallParts.Cast<MessagePart>().ToArray());
    }

    private TestApprovalService GetTestApprovalService()
    {
        return _serviceProvider.GetRequiredService<TestApprovalService>();
    }

    public override void Dispose()
    {
        _serviceProvider?.Dispose();
        base.Dispose();
    }
}