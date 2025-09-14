using System.CommandLine;
using System.Text.Json;
using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CodePunk.Infrastructure.Configuration;
using CodePunk.Console.Commands;
using Spectre.Console;
using Moq;
using Spectre.Console.Testing;
using Xunit;
using CodePunk.Core.Abstractions;
using CodePunk.Core.Models;
using CodePunk.Console.Configuration;

namespace CodePunk.Console.Tests.Commands;

public class PlanCreateFromSessionTests
{
    [Fact]
    public async Task Plan_Create_FromSession_Json_Emits_Schema()
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

    // fake summarizer
    var fakeSummary = new SessionSummary { Goal = "Add logging", CandidateFiles = new List<string>{"src/Log.cs"}, Rationale = "Reason", Truncated = false, UsedMessages = 3, TotalMessages = 3 };
    var summMock = new Mock<ISessionSummarizer>();
    summMock.Setup(s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<SessionSummaryOptions>(), It.IsAny<CancellationToken>())).ReturnsAsync(fakeSummary);
    builder.Services.AddSingleton<ISessionSummarizer>(summMock.Object);

    var host = builder.Build();
    var root = RootCommandFactory.Create(host.Services);

        try
        {
            var code = await root.InvokeAsync(new[] { "plan", "create", "--from-session", "--json" });
            Assert.Equal(0, code);
            var text = testConsole.Output;
            text = System.Text.RegularExpressions.Regex.Replace(text, "\u001B\\[[0-9;]*[A-Za-z]", string.Empty);
            var doc = JsonDocument.Parse(text.Trim());
            var rootEl = doc.RootElement;
            Assert.Equal("plan.create.fromSession.v1", rootEl.GetProperty("schema").GetString());
            Assert.Equal("Add logging", rootEl.GetProperty("goal").GetString());
            Assert.Equal("src/Log.cs", rootEl.GetProperty("candidateFiles").EnumerateArray().First().GetString());
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public async Task Plan_Create_FromSession_Json_Includes_TokenUsageApprox()
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

        // design summary with multiple files to exercise char math
        var fakeSummary = new SessionSummary
        {
            Goal = "Implement caching layer",
            CandidateFiles = new List<string> { "src/Cache/MemoryCache.cs", "src/Cache/ICache.cs" },
            Rationale = "Speed up repeated lookups",
            Truncated = false,
            UsedMessages = 5,
            TotalMessages = 5
        };
        var summMock = new Mock<ISessionSummarizer>();
        summMock.Setup(s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<SessionSummaryOptions>(), It.IsAny<CancellationToken>())).ReturnsAsync(fakeSummary);
        builder.Services.AddSingleton<ISessionSummarizer>(summMock.Object);

        var host = builder.Build();
        var root = RootCommandFactory.Create(host.Services);

        try
        {
            var code = await root.InvokeAsync(new[] { "plan", "create", "--from-session", "--json" });
            Assert.Equal(0, code);
            var text = testConsole.Output;
            text = System.Text.RegularExpressions.Regex.Replace(text, "\u001B\\[[0-9;]*[A-Za-z]", string.Empty);
            var doc = JsonDocument.Parse(text.Trim());
            var rootEl = doc.RootElement;
            var tu = rootEl.GetProperty("tokenUsageApprox");
            var sampleChars = tu.GetProperty("sampleChars").GetInt32();
            var approxTokens = tu.GetProperty("approxTokens").GetInt32();
            // recompute expected sampleChars
            var expectedSampleChars = (fakeSummary.Goal!.Length) + (fakeSummary.Rationale!.Length) + fakeSummary.CandidateFiles.Sum(f => f.Length + 1);
            Assert.Equal(expectedSampleChars, sampleChars);
            Assert.Equal(expectedSampleChars / 4, approxTokens);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public async Task Plan_Create_FromSession_Persists_Summary_With_TokenUsage()
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

        var fakeSummary = new SessionSummary
        {
            Goal = "Refactor auth module",
            CandidateFiles = new List<string> { "src/Auth/AuthService.cs" },
            Rationale = "Improve testability",
            Truncated = false,
            UsedMessages = 7,
            TotalMessages = 7
        };
        var summMock = new Mock<ISessionSummarizer>();
        summMock.Setup(s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<SessionSummaryOptions>(), It.IsAny<CancellationToken>())).ReturnsAsync(fakeSummary);
        builder.Services.AddSingleton<ISessionSummarizer>(summMock.Object);

        var host = builder.Build();
        var root = RootCommandFactory.Create(host.Services);
        string planId = string.Empty;
        try
        {
            var code = await root.InvokeAsync(new[] { "plan", "create", "--from-session", "--json" });
            Assert.Equal(0, code);
            var text = testConsole.Output;
            text = System.Text.RegularExpressions.Regex.Replace(text, "\u001B\\[[0-9;]*[A-Za-z]", string.Empty);
            var doc = JsonDocument.Parse(text.Trim());
            planId = doc.RootElement.GetProperty("planId").GetString()!;
            var store = host.Services.GetRequiredService<CodePunk.Console.Stores.IPlanFileStore>();
            var rec = await store.GetAsync(planId);
            Assert.NotNull(rec);
            Assert.NotNull(rec!.Summary);
            Assert.Equal("session", rec.Summary!.Source);
            Assert.Equal(fakeSummary.Goal, rec.Summary.Goal);
            Assert.Single(rec.Summary.CandidateFiles);
            Assert.Equal(fakeSummary.CandidateFiles[0], rec.Summary.CandidateFiles[0]);
            Assert.Equal(fakeSummary.Rationale, rec.Summary.Rationale);
            Assert.Equal(fakeSummary.UsedMessages, rec.Summary.UsedMessages);
            Assert.Equal(fakeSummary.TotalMessages, rec.Summary.TotalMessages);
            Assert.NotNull(rec.Summary.TokenUsage);
            var expectedSampleChars = (fakeSummary.Goal!.Length) + (fakeSummary.Rationale!.Length) + fakeSummary.CandidateFiles.Sum(f => f.Length + 1);
            Assert.Equal(expectedSampleChars, rec.Summary.TokenUsage!.SampleChars);
            Assert.Equal(expectedSampleChars / 4, rec.Summary.TokenUsage.ApproxTokens);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }
}
