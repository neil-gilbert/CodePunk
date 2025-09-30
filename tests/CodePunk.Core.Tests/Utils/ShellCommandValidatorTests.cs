using CodePunk.Core.Utils;
using FluentAssertions;
using Xunit;

namespace CodePunk.Core.Tests.Utils;

public class ShellCommandValidatorTests
{
    [Theory]
    [InlineData("echo hello")]
    [InlineData("ls -la")]
    [InlineData("git status")]
    [InlineData("npm install")]
    public void ContainsCommandSubstitution_SafeCommands_ReturnsFalse(string command)
    {
        var result = ShellCommandValidator.ContainsCommandSubstitution(command);
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("echo $(whoami)")]
    [InlineData("cat <(echo test)")]
    [InlineData("tee >(cat)")]
    [InlineData("echo `whoami`")]
    [InlineData("echo `date`")]
    public void ContainsCommandSubstitution_DangerousCommands_ReturnsTrue(string command)
    {
        var result = ShellCommandValidator.ContainsCommandSubstitution(command);
        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsCommandSubstitution_CommandSubstitutionInSingleQuotes_ReturnsFalse()
    {
        var command = "echo '$(whoami)'";
        var result = ShellCommandValidator.ContainsCommandSubstitution(command);
        result.Should().BeFalse();
    }

    [Fact]
    public void ContainsCommandSubstitution_CommandSubstitutionInDoubleQuotes_ReturnsTrue()
    {
        var command = "echo \"$(whoami)\"";
        var result = ShellCommandValidator.ContainsCommandSubstitution(command);
        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsCommandSubstitution_EscapedDollarSign_ReturnsFalse()
    {
        var command = "echo \\$(whoami)";
        var result = ShellCommandValidator.ContainsCommandSubstitution(command);
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("echo hello && echo world", new[] { "echo hello", "echo world" })]
    [InlineData("ls -la || pwd", new[] { "ls -la", "pwd" })]
    [InlineData("git add . ; git commit", new[] { "git add .", "git commit" })]
    [InlineData("npm install && npm test && npm build", new[] { "npm install", "npm test", "npm build" })]
    public void SplitCommandChain_ChainedCommands_SplitsCorrectly(string command, string[] expected)
    {
        var result = ShellCommandValidator.SplitCommandChain(command);
        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void SplitCommandChain_CommandWithQuotedOperator_DoesNotSplit()
    {
        var command = "echo 'hello && world'";
        var result = ShellCommandValidator.SplitCommandChain(command);
        result.Should().ContainSingle();
        result[0].Should().Be("echo 'hello && world'");
    }

    [Theory]
    [InlineData("ls", "ls")]
    [InlineData("git status", "git")]
    [InlineData("npm install --save", "npm")]
    [InlineData("/usr/bin/python3", "python3")]
    [InlineData("/bin/bash", "bash")]
    public void GetCommandRoot_VariousCommands_ExtractsRoot(string command, string expectedRoot)
    {
        var result = ShellCommandValidator.GetCommandRoot(command);
        result.Should().Be(expectedRoot);
    }

    [Fact]
    public void GetCommandRoot_QuotedCommand_ExtractsRoot()
    {
        var command = "\"git\" status";
        var result = ShellCommandValidator.GetCommandRoot(command);
        result.Should().Be("git");
    }

    [Theory]
    [InlineData("", new string[] { })]
    [InlineData("   ", new string[] { })]
    public void GetCommandRoots_EmptyCommand_ReturnsEmpty(string command, string[] expected)
    {
        var result = ShellCommandValidator.GetCommandRoots(command);
        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void GetCommandRoots_ChainedCommands_ReturnsAllRoots()
    {
        var command = "git add . && git commit && npm test";
        var result = ShellCommandValidator.GetCommandRoots(command);
        result.Should().BeEquivalentTo(new[] { "git", "git", "npm" });
    }

    [Fact]
    public void ValidateCommand_SafeCommand_ReturnsValid()
    {
        var command = "echo hello";
        var result = ShellCommandValidator.ValidateCommand(command);

        result.IsValid.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void ValidateCommand_CommandSubstitution_ReturnsInvalid()
    {
        var command = "echo $(whoami)";
        var result = ShellCommandValidator.ValidateCommand(command);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Command substitution");
        result.ErrorMessage.Should().Contain("not allowed for security");
    }

    [Fact]
    public void ValidateCommand_EmptyCommand_ReturnsInvalid()
    {
        var command = "";
        var result = ShellCommandValidator.ValidateCommand(command);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("cannot be empty");
    }

    [Fact]
    public void ValidateCommand_BlockedCommand_ReturnsInvalid()
    {
        var command = "rm -rf /";
        var blockedCommands = new List<string> { "rm" };
        var result = ShellCommandValidator.ValidateCommand(command, blockedCommands: blockedCommands);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("blocked");
        result.BlockedCommand.Should().Be("rm");
    }

    [Fact]
    public void ValidateCommand_AllowedCommandsConfigured_AllowedCommandIsValid()
    {
        var command = "git status";
        var allowedCommands = new List<string> { "git", "npm" };
        var result = ShellCommandValidator.ValidateCommand(command, allowedCommands: allowedCommands);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateCommand_AllowedCommandsConfigured_DisallowedCommandIsInvalid()
    {
        var command = "rm -rf /";
        var allowedCommands = new List<string> { "git", "npm" };
        var result = ShellCommandValidator.ValidateCommand(command, allowedCommands: allowedCommands);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not in the allowed commands");
        result.BlockedCommand.Should().Be("rm");
    }

    [Fact]
    public void ValidateCommand_BlocklistTakesPrecedence_ReturnsInvalid()
    {
        var command = "git push --force";
        var allowedCommands = new List<string> { "git" };
        var blockedCommands = new List<string> { "git push --force" };
        var result = ShellCommandValidator.ValidateCommand(command, allowedCommands, blockedCommands);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("blocked");
    }

    [Fact]
    public void ValidateCommand_ChainedCommandWithOneBlocked_ReturnsInvalid()
    {
        var command = "echo test && rm file.txt";
        var blockedCommands = new List<string> { "rm" };
        var result = ShellCommandValidator.ValidateCommand(command, blockedCommands: blockedCommands);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("blocked");
        result.BlockedCommand.Should().Be("rm");
    }

    [Fact]
    public void ValidateCommand_CommandPrefixMatching_Works()
    {
        var command = "git push origin main";
        var allowedCommands = new List<string> { "git" };
        var result = ShellCommandValidator.ValidateCommand(command, allowedCommands: allowedCommands);

        result.IsValid.Should().BeTrue();
    }
}
