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
}
