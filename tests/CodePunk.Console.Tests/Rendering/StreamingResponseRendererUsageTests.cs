using CodePunk.Console.Rendering;
using CodePunk.Core.Chat;
using Moq;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

namespace CodePunk.Console.Tests.Rendering;

public class StreamingResponseRendererUsageTests
{
    [Fact]
    public void CompleteStreaming_ShouldEmitUsageSummary_WhenTokensProvided()
    {
        var testConsole = new TestConsole();
        var renderer = new StreamingResponseRenderer(testConsole);
        renderer.StartStreaming();
        renderer.ProcessChunk(new ChatStreamChunk
        {
            ContentDelta = "Hello world",
            IsComplete = false
        });
        renderer.ProcessChunk(new ChatStreamChunk
        {
            ContentDelta = "!",
            IsComplete = true,
            InputTokens = 12,
            OutputTokens = 34,
            EstimatedCost = 0.0123m
        });
        renderer.CompleteStreaming();
        var output = testConsole.Output;
        Assert.Contains("tokens:", output);
    }
}
