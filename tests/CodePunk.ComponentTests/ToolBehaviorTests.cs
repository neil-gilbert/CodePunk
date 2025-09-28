using System.Text.Json;
using CodePunk.ComponentTests.TestHelpers;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models.FileEdit;
using CodePunk.Core.Services;
using CodePunk.Core.Tools;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodePunk.ComponentTests;

/// <summary>
/// End-to-end behavioral tests for tools - verifying that what AI "wants to do" actually happens
/// These test the outer boundary: AI tool calls → file system changes
/// </summary>
public class ToolBehaviorTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _testWorkspace;

    public ToolBehaviorTests()
    {
        _testWorkspace = Path.Combine(Path.GetTempPath(), $"codepunk_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testWorkspace);
        Environment.CurrentDirectory = _testWorkspace;

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task AI_CallsWriteFileTool_WithValidPath_FileIsCreatedOnDisk()
    {
        // Arrange: AI wants to write a file
        var writeFileTool = _serviceProvider.GetRequiredService<WriteFileTool>();
        var approvalService = GetTestApprovalService();
        approvalService.AutoApprove = true;

        var arguments = JsonDocument.Parse(@"{
            ""file_path"": ""ai_created.txt"",
            ""content"": ""This file was created by AI"",
            ""require_approval"": true
        }").RootElement;

        // Act: AI executes the tool
        var result = await writeFileTool.ExecuteAsync(arguments);

        // Assert: Verify actual file system changes
        result.IsError.Should().BeFalse();
        result.UserCancelled.Should().BeFalse();

        // Most important: File actually exists on disk
        File.Exists("ai_created.txt").Should().BeTrue();
        var actualContent = await File.ReadAllTextAsync("ai_created.txt");
        actualContent.Should().Be("This file was created by AI");

        result.Content.Should().Contain("Successfully");
    }

    [Fact]
    public async Task AI_CallsWriteFileTool_UserCancels_NoFileCreated_ToolReturnsUserCancelled()
    {
        // Arrange: AI wants to write a file, but user will cancel
        var writeFileTool = _serviceProvider.GetRequiredService<WriteFileTool>();
        var approvalService = GetTestApprovalService();
        approvalService.CancelAll = true;

        var arguments = JsonDocument.Parse(@"{
            ""file_path"": ""cancelled_file.txt"",
            ""content"": ""This should not be written"",
            ""require_approval"": true
        }").RootElement;

        // Act: AI tries to execute the tool
        var result = await writeFileTool.ExecuteAsync(arguments);

        // Assert: Verify cancellation behavior
        result.UserCancelled.Should().BeTrue();
        result.IsError.Should().BeFalse();

        // Most important: No file created on disk
        File.Exists("cancelled_file.txt").Should().BeFalse();

        result.Content.Should().Contain("cancelled");
    }

    [Fact]
    public async Task AI_CallsReplaceInFileTool_WithExistingFile_FileIsModifiedOnDisk()
    {
        // Arrange: Create existing file and setup AI to modify it
        var fileName = "existing_file.txt";
        var originalContent = "Hello, World!\nThis is line 2\nThis is line 3";
        await File.WriteAllTextAsync(fileName, originalContent);

        var replaceInFileTool = _serviceProvider.GetRequiredService<ReplaceInFileTool>();
        var approvalService = GetTestApprovalService();
        approvalService.AutoApprove = true;

        var arguments = JsonDocument.Parse(@"{
            ""file_path"": ""existing_file.txt"",
            ""old_string"": ""Hello, World!"",
            ""new_string"": ""Hello, Universe!"",
            ""require_approval"": true
        }").RootElement;

        // Act: AI executes the replacement tool
        var result = await replaceInFileTool.ExecuteAsync(arguments);

        // Assert: Verify actual file modification
        result.IsError.Should().BeFalse();
        result.UserCancelled.Should().BeFalse();

        // Most important: File actually modified on disk
        var actualContent = await File.ReadAllTextAsync(fileName);
        actualContent.Should().Contain("Hello, Universe!");
        actualContent.Should().NotContain("Hello, World!");
        actualContent.Should().Contain("This is line 2"); // Other content preserved

        result.Content.Should().Contain("Successfully");
    }

    [Fact]
    public async Task AI_CallsWriteFileTool_WithInvalidPath_ToolReturnsError_NoFileCreated()
    {
        // Arrange: AI tries to write outside workspace
        var writeFileTool = _serviceProvider.GetRequiredService<WriteFileTool>();

        var arguments = JsonDocument.Parse(@"{
            ""file_path"": ""../outside_workspace.txt"",
            ""content"": ""This should not be written"",
            ""require_approval"": false
        }").RootElement;

        // Act: AI tries to execute invalid tool call
        var result = await writeFileTool.ExecuteAsync(arguments);

        // Assert: Verify error handling
        result.IsError.Should().BeTrue();

        // Most important: No file created outside workspace
        File.Exists("../outside_workspace.txt").Should().BeFalse();

        result.Content.Should().Contain("Invalid file path");
    }

    [Fact]
    public async Task AI_CallsReplaceInFileTool_WithNonExistentFile_ToolReturnsError()
    {
        // Arrange: AI tries to modify non-existent file
        var replaceInFileTool = _serviceProvider.GetRequiredService<ReplaceInFileTool>();

        var arguments = JsonDocument.Parse(@"{
            ""file_path"": ""nonexistent.txt"",
            ""old_string"": ""anything"",
            ""new_string"": ""replacement"",
            ""require_approval"": false
        }").RootElement;

        // Act: AI tries to execute tool on non-existent file
        var result = await replaceInFileTool.ExecuteAsync(arguments);

        // Assert: Verify error handling
        result.IsError.Should().BeTrue();
        (result.Content.Contains("not found") || result.Content.Contains("does not exist"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task AI_CallsReplaceInFileTool_TextNotFound_ToolReturnsError()
    {
        // Arrange: Create file but AI tries to replace text that doesn't exist
        var fileName = "test_file.txt";
        await File.WriteAllTextAsync(fileName, "Some existing content");

        var replaceInFileTool = _serviceProvider.GetRequiredService<ReplaceInFileTool>();

        var arguments = JsonDocument.Parse(@"{
            ""file_path"": ""test_file.txt"",
            ""old_string"": ""text that does not exist"",
            ""new_string"": ""replacement"",
            ""require_approval"": false
        }").RootElement;

        // Act: AI tries to replace non-existent text
        var result = await replaceInFileTool.ExecuteAsync(arguments);

        // Assert: Verify error handling and no file modification
        result.IsError.Should().BeTrue();

        // File should remain unchanged
        var actualContent = await File.ReadAllTextAsync(fileName);
        actualContent.Should().Be("Some existing content");

        (result.Content.Contains("not found") || result.Content.Contains("No occurrences"))
            .Should().BeTrue();
    }

    private void ConfigureServices(ServiceCollection services)
    {
        // Register the actual tools we want to test
        services.AddScoped<WriteFileTool>();
        services.AddScoped<ReplaceInFileTool>();

        // Register real file services
        services.AddScoped<IFileEditService, FileEditService>();
        services.AddScoped<IDiffService, DiffService>();

        // Test doubles for user interactions
        services.AddScoped<TestApprovalService>();
        services.AddScoped<IApprovalService>(provider => provider.GetRequiredService<TestApprovalService>());

        // Required loggers
        services.AddSingleton<ILogger<FileEditService>>(provider => NullLogger<FileEditService>.Instance);
        services.AddSingleton<ILogger<DiffService>>(provider => NullLogger<DiffService>.Instance);
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