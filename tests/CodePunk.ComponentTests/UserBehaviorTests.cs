using System.Text.Json;
using CodePunk.ComponentTests.TestHelpers;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Chat;
using CodePunk.Core.Models;
using CodePunk.Core.Models.FileEdit;
using CodePunk.Core.Services;
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
public class UserBehaviorTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<ILLMService> _mockLLMService;
    private readonly string _testWorkspace;
    private readonly List<Message> _capturedLLMMessages = new();

    public UserBehaviorTests()
    {
        _testWorkspace = Path.Combine(Path.GetTempPath(), $"codepunk_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testWorkspace);
        Environment.CurrentDirectory = _testWorkspace;

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
    }

    private void SetupLLMServiceCapture()
    {
        _mockLLMService.Setup(x => x.SendMessageAsync(It.IsAny<IList<Message>>(), It.IsAny<CancellationToken>()))
            .Callback<IList<Message>, CancellationToken>((messages, _) =>
            {
                _capturedLLMMessages.AddRange(messages);
            })
            .Returns<IList<Message>, CancellationToken>((messages, _) =>
            {
                // Return the last AI response that was set up
                return Task.FromResult(_currentAIResponse);
            });
    }

    private Message _currentAIResponse = Message.Create("test", MessageRole.Assistant, new[] { new TextPart("Default response") });

    private void SetupAIToCallWriteFileTool(string fileName, string content)
    {
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

    public void Dispose()
    {
        _serviceProvider?.Dispose();
        if (Directory.Exists(_testWorkspace))
        {
            Directory.Delete(_testWorkspace, recursive: true);
        }
    }
}