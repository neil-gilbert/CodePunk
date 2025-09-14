using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Testing;
using Spectre.Console;
using Xunit;
using CodePunk.Console.Commands;
using CodePunk.Console.Configuration;
using CodePunk.Infrastructure.Configuration;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using Moq;
using System.CommandLine;

namespace CodePunk.Console.Tests.Commands;

public class PlanShowJsonTests
{
    [Fact]
    public async Task Plan_Show_Json_Includes_Summary_With_Rationale_And_TokenUsage()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "codepunk-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", tmp);
        Directory.CreateDirectory(tmp);
    var testConsole = new TestConsole();
    testConsole.Profile.Width = 5000;
        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
        builder.Services.AddCodePunkServices(builder.Configuration);
        builder.Services.AddCodePunkConsole();
        builder.Services.AddSingleton<IAnsiConsole>(sp => testConsole);

        // mock summarizer so create-from-session works deterministically
        var fakeSummary = new SessionSummary
        {
            Goal = "Add caching",
            CandidateFiles = new List<string> { "src/Cache/CacheService.cs" },
            Rationale = "Improve latency",
            Truncated = false,
            UsedMessages = 4,
            TotalMessages = 4
        };
        var summMock = new Mock<ISessionSummarizer>();
        summMock.Setup(s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<SessionSummaryOptions>(), It.IsAny<CancellationToken>())).ReturnsAsync(fakeSummary);
        builder.Services.AddSingleton<ISessionSummarizer>(summMock.Object);

        var host = builder.Build();
        var root = RootCommandFactory.Create(host.Services);

        try
        {
            var createCode = await root.InvokeAsync(new[] { "plan", "create", "--from-session", "--json" });
            Assert.Equal(0, createCode);
            var createJson = System.Text.RegularExpressions.Regex.Replace(testConsole.Output, "\u001B\\[[0-9;]*[A-Za-z]", string.Empty).Trim();
            var createDoc = JsonDocument.Parse(createJson);
            var planId = createDoc.RootElement.GetProperty("planId").GetString()!;
            // create a new console instance for show to avoid mixing outputs
            var showConsole = new TestConsole();
            showConsole.Profile.Width = 5000;
            var services = host.Services as ServiceProvider;
            // rebuild root with new console for clean capture
            var builder2 = Host.CreateApplicationBuilder(Array.Empty<string>());
            builder2.Services.AddCodePunkServices(builder2.Configuration);
            builder2.Services.AddCodePunkConsole();
            builder2.Services.AddSingleton<IAnsiConsole>(sp => showConsole);
            // reuse summarizer mock
            builder2.Services.AddSingleton<ISessionSummarizer>(summMock.Object);
            var host2 = builder2.Build();
            var root2 = RootCommandFactory.Create(host2.Services);
            var showCode = await root2.InvokeAsync(new[] { "plan", "show", "--id", planId, "--json" });
            Assert.Equal(0, showCode);
            var showJson = System.Text.RegularExpressions.Regex.Replace(showConsole.Output, "\u001B\\[[0-9;]*[A-Za-z]", string.Empty).Trim();
            var showDoc = JsonDocument.Parse(showJson);
            var rootEl = showDoc.RootElement;
            Assert.Equal("plan.show.v1", rootEl.GetProperty("schema").GetString());
            var summary = rootEl.GetProperty("summary");
            Assert.Equal("Add caching", summary.GetProperty("Goal").GetString());
            Assert.Equal("Improve latency", summary.GetProperty("Rationale").GetString());
            var tokenUsage = summary.GetProperty("tokenUsage");
            var sampleChars = tokenUsage.GetProperty("SampleChars").GetInt32();
            var approxTokens = tokenUsage.GetProperty("ApproxTokens").GetInt32();
            Assert.True(sampleChars > 0);
            Assert.True(approxTokens >= 0);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }
}
