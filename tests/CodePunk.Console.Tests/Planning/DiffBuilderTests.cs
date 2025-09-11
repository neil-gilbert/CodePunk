using CodePunk.Console.Planning;
using Xunit;

namespace CodePunk.Console.Tests.Planning;

public class DiffBuilderTests
{
    [Fact]
    public void Unified_ReturnsEmpty_WhenNoChanges()
    {
        var diff = DiffBuilder.Unified("line1\nline2\n", "line1\nline2\n", "file.txt");
        Assert.True(string.IsNullOrEmpty(diff));
    }

    [Fact]
    public void Unified_ShowsAddedAndRemoved()
    {
        var before = "a\nb\nc";
        var after = "a\nb\nX\nc";
        var diff = DiffBuilder.Unified(before, after, "f.txt");
        Assert.Contains("+X", diff);
        Assert.Contains("--- a/", diff);
        Assert.Contains("+++ b/", diff);
    }
}
