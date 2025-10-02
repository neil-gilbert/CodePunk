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
/// Simple component tests that verify core behaviors work end-to-end
/// These tests focus on user-visible outcomes rather than implementation details
/// </summary>
public class SimpleWorkflowTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<ILLMService> _mockLLMService;
    private readonly string _testWorkspace;
    private readonly string _originalDirectory;

    public SimpleWorkflowTests()
    {
        _originalDirectory = Environment.CurrentDirectory;
        _testWorkspace = Path.Combine(Path.GetTempPath(), $"codepunk_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testWorkspace);
        Environment.CurrentDirectory = _testWorkspace;

        _mockLLMService = new Mock<ILLMService>();

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task FileEditService_WriteFile_UserApproves_FileIsCreated()
    {
        // Arrange
        var fileName = "test.txt";
        var fileContent = "Hello, World!";
        var fileEditService = _serviceProvider.GetRequiredService<IFileEditService>();
        var approvalService = _serviceProvider.GetRequiredService<TestApprovalService>();
        approvalService.AutoApprove = true;

        var request = new WriteFileRequest(fileName, fileContent, RequireApproval: true);

        // Act
        var result = await fileEditService.WriteFileAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(fileName).Should().BeTrue();
        var actualContent = await File.ReadAllTextAsync(fileName);
        actualContent.Should().Be(fileContent);
    }

    [Fact]
    public async Task FileEditService_WriteFile_UserCancels_NoFileCreated()
    {
        // Arrange
        var fileName = "cancelled_test.txt";
        var fileContent = "This should not be written";
        var fileEditService = _serviceProvider.GetRequiredService<IFileEditService>();
        var approvalService = _serviceProvider.GetRequiredService<TestApprovalService>();
        approvalService.CancelAll = true;

        var request = new WriteFileRequest(fileName, fileContent, RequireApproval: true);

        // Act
        var result = await fileEditService.WriteFileAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("USER_CANCELLED");
        File.Exists(fileName).Should().BeFalse();
    }

    [Fact]
    public async Task FileEditService_ReplaceInFile_UpdatesFileCorrectly()
    {
        // Arrange
        var fileName = "replace_test.txt";
        var originalContent = "Hello, World!\nThis is a test.";
        var oldString = "World";
        var newString = "Universe";

        await File.WriteAllTextAsync(fileName, originalContent);

        var fileEditService = _serviceProvider.GetRequiredService<IFileEditService>();
        var approvalService = _serviceProvider.GetRequiredService<TestApprovalService>();
        approvalService.AutoApprove = true;

        var request = new ReplaceRequest(fileName, oldString, newString, ExpectedOccurrences: 1, RequireApproval: true);

        // Act
        var result = await fileEditService.ReplaceInFileAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        var actualContent = await File.ReadAllTextAsync(fileName);
        actualContent.Should().Contain("Hello, Universe!");
        actualContent.Should().NotContain("Hello, World!");
    }

    [Fact]
    public void DiffService_GeneratesDiffCorrectly()
    {
        // Arrange
        var originalContent = "Line 1\nLine 2\nLine 3";
        var newContent = "Line 1\nModified Line 2\nLine 3\nLine 4";
        var diffService = _serviceProvider.GetRequiredService<IDiffService>();

        // Act
        var diff = diffService.CreateUnifiedDiff("test_file.txt", originalContent, newContent);

        // Assert
        diff.Should().NotBeEmpty();
        diff.Should().Contain("Modified Line 2");
        diff.Should().Contain("Line 4");
    }

    [Fact]
    public void DiffService_ComputeStats_ReturnsCorrectCounts()
    {
        // Arrange
        var originalContent = "Line 1\nLine 2\nLine 3";
        var newContent = "Line 1\nModified Line 2\nLine 3\nLine 4";
        var diffService = _serviceProvider.GetRequiredService<IDiffService>();

        // Act
        var stats = diffService.ComputeStats(originalContent, newContent, newContent);

        // Assert
        stats.LinesAdded.Should().Be(2); // Modified line + new line
        stats.LinesRemoved.Should().Be(1); // Original Line 2
    }

    private void ConfigureServices(ServiceCollection services)
    {
        // Register real services we want to test
        services.AddScoped<IFileEditService, FileEditService>();
        services.AddScoped<IDiffService, DiffService>();

        // Register test doubles
        services.AddScoped<TestApprovalService>();
        services.AddScoped<IApprovalService>(provider => provider.GetRequiredService<TestApprovalService>());

        // Mock external dependencies
        services.AddSingleton(_mockLLMService.Object);

        // Add all required loggers
        services.AddSingleton<ILogger<InteractiveChatSession>>(provider => NullLogger<InteractiveChatSession>.Instance);
        services.AddSingleton<ILogger<FileEditService>>(provider => NullLogger<FileEditService>.Instance);
        services.AddSingleton<ILogger<DiffService>>(provider => NullLogger<DiffService>.Instance);
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
        if (Directory.Exists(_originalDirectory))
        {
            Environment.CurrentDirectory = _originalDirectory;
        }
        if (Directory.Exists(_testWorkspace))
        {
            Directory.Delete(_testWorkspace, recursive: true);
        }
    }
}

