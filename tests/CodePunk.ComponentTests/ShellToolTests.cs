using System.Runtime.InteropServices;
using System.Text.Json;
using CodePunk.Core.Configuration;
using CodePunk.Core.Services;
using CodePunk.Core.Tools;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CodePunk.ComponentTests;

public class ShellToolTests : IDisposable
{
    private readonly string _testWorkspace;

    public ShellToolTests()
    {
        _testWorkspace = Path.Combine(Path.GetTempPath(), $"codepunk_shell_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testWorkspace);
        Environment.CurrentDirectory = _testWorkspace;
    }

    [Fact]
    public async Task ExecuteAsync_SimpleEchoCommand_ReturnsOutput()
    {
        var options = CreateDefaultOptions();
        var tool = new ShellTool(options);

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var command = isWindows ? "echo Hello World" : "echo 'Hello World'";

        var arguments = JsonDocument.Parse($@"{{
            ""command"": ""{command}"",
            ""description"": ""Test echo command""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("Hello World");
        result.Content.Should().Contain("Command:");
        result.Content.Should().Contain("Description: Test echo command");
        result.Content.Should().Contain("Exit Code: 0");
    }

    [Fact]
    public async Task ExecuteAsync_CommandWithWorkingDirectory_ExecutesInCorrectDirectory()
    {
        var options = CreateDefaultOptions();
        var tool = new ShellTool(options);

        var testDir = Path.Combine(_testWorkspace, "subdir");
        Directory.CreateDirectory(testDir);

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var command = isWindows ? "cd" : "pwd";

        var arguments = JsonDocument.Parse($@"{{
            ""command"": ""{command}"",
            ""directory"": ""{testDir.Replace("\\", "\\\\")}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain(testDir);
    }

    [Fact]
    public async Task ExecuteAsync_CommandSubstitution_ReturnsError()
    {
        var options = CreateDefaultOptions();
        var tool = new ShellTool(options);

        var arguments = JsonDocument.Parse(@"{
            ""command"": ""echo $(whoami)""
        }").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeTrue();
        result.ErrorMessage.Should().Contain("Command substitution");
        result.ErrorMessage.Should().Contain("not allowed for security");
    }

    [Fact]
    public async Task ExecuteAsync_BackticksCommandSubstitution_ReturnsError()
    {
        var options = CreateDefaultOptions();
        var tool = new ShellTool(options);

        var arguments = JsonDocument.Parse(@"{
            ""command"": ""echo `whoami`""
        }").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeTrue();
        result.ErrorMessage.Should().Contain("Command substitution");
    }

    [Fact]
    public async Task ExecuteAsync_ProcessSubstitution_ReturnsError()
    {
        var options = CreateDefaultOptions();
        var tool = new ShellTool(options);

        var arguments = JsonDocument.Parse(@"{
            ""command"": ""cat <(echo test)""
        }").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeTrue();
        result.ErrorMessage.Should().Contain("Command substitution");
    }

    [Fact]
    public async Task ExecuteAsync_BlockedCommand_ReturnsError()
    {
        var optionsValue = new ShellCommandOptions
        {
            EnableCommandValidation = true,
            BlockedCommands = new List<string> { "rm", "del" }
        };
        var options = Options.Create(optionsValue);
        var tool = new ShellTool(options);

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var command = isWindows ? "del test.txt" : "rm test.txt";

        var arguments = JsonDocument.Parse($@"{{
            ""command"": ""{command}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeTrue();
        result.ErrorMessage.Should().Contain("blocked");
    }

    [Fact]
    public async Task ExecuteAsync_AllowedCommandsConfigured_OnlyAllowedCommandsExecute()
    {
        var optionsValue = new ShellCommandOptions
        {
            EnableCommandValidation = true,
            AllowedCommands = new List<string> { "echo" }
        };
        var options = Options.Create(optionsValue);
        var tool = new ShellTool(options);

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var allowedCommand = isWindows ? "echo Allowed" : "echo 'Allowed'";

        var allowedArgs = JsonDocument.Parse($@"{{
            ""command"": ""{allowedCommand}""
        }}").RootElement;

        var allowedResult = await tool.ExecuteAsync(allowedArgs);
        allowedResult.IsError.Should().BeFalse();

        var disallowedCommand = isWindows ? "dir" : "ls";
        var disallowedArgs = JsonDocument.Parse($@"{{
            ""command"": ""{disallowedCommand}""
        }}").RootElement;

        var disallowedResult = await tool.ExecuteAsync(disallowedArgs);
        disallowedResult.IsError.Should().BeTrue();
        disallowedResult.ErrorMessage.Should().Contain("not in the allowed commands");
    }

    [Fact]
    public async Task ExecuteAsync_CommandWithStderr_CapturesBothStreams()
    {
        var options = CreateDefaultOptions();
        var tool = new ShellTool(options);

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var command = isWindows
            ? "echo stdout && echo stderr 1>&2"
            : "echo stdout && echo stderr >&2";

        var arguments = JsonDocument.Parse($@"{{
            ""command"": ""{command}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.Content.Should().Contain("Stdout:");
        result.Content.Should().Contain("Stderr:");
        result.Content.Should().Contain("stdout");
        result.Content.Should().Contain("stderr");
    }

    [Fact]
    public async Task ExecuteAsync_CommandWithNonZeroExitCode_MarkedAsError()
    {
        var options = CreateDefaultOptions();
        var tool = new ShellTool(options);

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var command = isWindows ? "exit 1" : "exit 1";

        var arguments = JsonDocument.Parse($@"{{
            ""command"": ""{command}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeTrue();
        result.Content.Should().Contain("Exit Code: 1");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyCommand_ReturnsError()
    {
        var options = CreateDefaultOptions();
        var tool = new ShellTool(options);

        var arguments = JsonDocument.Parse(@"{
            ""command"": """"
        }").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeTrue();
        result.ErrorMessage.Should().Contain("cannot be empty");
    }

    [Fact]
    public async Task ExecuteAsync_MissingCommandParameter_ReturnsError()
    {
        var options = CreateDefaultOptions();
        var tool = new ShellTool(options);

        var arguments = JsonDocument.Parse(@"{
            ""description"": ""Test""
        }").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeTrue();
        result.ErrorMessage.Should().Contain("command parameter is required");
    }

    [Fact]
    public async Task ExecuteAsync_CommandChainWithOneBlocked_ReturnsError()
    {
        var optionsValue = new ShellCommandOptions
        {
            EnableCommandValidation = true,
            BlockedCommands = new List<string> { "rm", "del" }
        };
        var options = Options.Create(optionsValue);
        var tool = new ShellTool(options);

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var command = isWindows
            ? "echo test && del test.txt"
            : "echo test && rm test.txt";

        var arguments = JsonDocument.Parse($@"{{
            ""command"": ""{command}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeTrue();
        result.ErrorMessage.Should().Contain("blocked");
    }

    [Fact]
    public async Task ExecuteAsync_ValidationDisabled_AllowsAnyCommand()
    {
        var optionsValue = new ShellCommandOptions
        {
            EnableCommandValidation = false,
            BlockedCommands = new List<string> { "echo" }
        };
        var options = Options.Create(optionsValue);
        var tool = new ShellTool(options);

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var command = isWindows ? "echo test" : "echo 'test'";

        var arguments = JsonDocument.Parse($@"{{
            ""command"": ""{command}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_EnvironmentVariableSet_CodePunkCliIsSet()
    {
        var options = CreateDefaultOptions();
        var tool = new ShellTool(options);

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var command = isWindows ? "echo %CODEPUNK_CLI%" : "echo $CODEPUNK_CLI";

        var arguments = JsonDocument.Parse($@"{{
            ""command"": ""{command}""
        }}").RootElement;

        var result = await tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("1");
    }

    private IOptions<ShellCommandOptions> CreateDefaultOptions()
    {
        return Options.Create(new ShellCommandOptions
        {
            EnableCommandValidation = true,
            AllowedCommands = new List<string>(),
            BlockedCommands = new List<string>()
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_testWorkspace))
        {
            Directory.Delete(_testWorkspace, recursive: true);
        }
    }
}
