using System;
using System.Linq;
using System.Threading.Tasks;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Services;
using CodePunk.Tui.Rendering;
using FluentAssertions;
using Xunit;

namespace CodePunk.ComponentTests;

public class DiffViewerFormattingTests
{
    private readonly IDiffService _diffService = new DiffService();

    [Fact]
    public void BuildDisplayLines_ShouldProduceSingleLogicalRowPerLine()
    {
        // Arrange
        var before = "alpha\n";
        var after  = "alpha\nbravo\n"; // one added line
        var diff = _diffService.CreateUnifiedDiff("file.txt", before, after);

        // Act
        var lines = DiffLineFormatter.BuildDisplayLines(diff, context: 1, maxLines: 25).ToArray();

        // Assert
        lines.Should().NotBeEmpty();
        lines.Should().OnlyContain(l => !l.Contains('\n') && !l.Contains('\r'));
        // Exactly one added line with + indicator and content
        lines.Count(l => l.Contains(" + ")).Should().Be(1);
        lines.Any(l => l.EndsWith("bravo")).Should().BeTrue();
    }

    [Fact]
    public void BuildDisplayLines_ShouldTruncateToMaxLines()
    {
        // Arrange
        var before = string.Empty;
        var after = string.Join('\n', Enumerable.Range(1, 100).Select(i => $"line{i}")) + "\n"; // 100 additions
        var diff = _diffService.CreateUnifiedDiff("big.txt", before, after);

        // Act
        var lines = DiffLineFormatter.BuildDisplayLines(diff, context: 0, maxLines: 25);

        // Assert
        lines.Count.Should().Be(25);
        lines.First().TrimStart().StartsWith("+ ").Should().BeTrue();
        lines.Last().TrimStart().StartsWith("+ ").Should().BeTrue();
    }
}

