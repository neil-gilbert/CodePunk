using System.Threading.Tasks;
using CodePunk.Console.Commands;
using Xunit;

namespace CodePunk.Console.Tests.Commands;

public class NewCommandTests
{
    [Fact]
    public async Task ExecuteAsync_TrimsLongTitle()
    {
        var cmd = new NewCommand();
        var longWords = new string('a', 50) + " " + new string('b', 50); // 101 chars with space
        var result = await cmd.ExecuteAsync(new[]{ longWords });
    Assert.Contains(new string('a', 50), result.Message!); // first segment retained
        Assert.True(result.Message.Length < 140); // trimmed to <=80 plus markup
    }

    [Fact]
    public async Task ExecuteAsync_DefaultsWhenNoArgs()
    {
        var cmd = new NewCommand();
        var result = await cmd.ExecuteAsync(Array.Empty<string>());
        Assert.Contains("Chat Session", result.Message);
    }
}
