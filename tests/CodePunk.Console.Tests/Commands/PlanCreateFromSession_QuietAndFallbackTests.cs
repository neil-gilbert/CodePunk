using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console.Testing;
using Xunit;
using System.CommandLine;
using System.CommandLine.Invocation;
using CodePunk.Infrastructure.Configuration;
using CodePunk.Console.Configuration;
using CodePunk.Console.Commands;
using Spectre.Console;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CodePunk.Console.Tests.Commands;

public class PlanCreateFromSession_QuietAndFallbackTests
{
    [Fact]
    public async Task PlanCreate_FromSession_NoSummarizer_EmitsErrorJson()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "codepunk-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", tmp);
        Directory.CreateDirectory(tmp);

        var testConsole = new TestConsole();
        testConsole.Profile.Width = 5000;
    var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
    builder.Services.AddCodePunkServices(builder.Configuration);
    builder.Services.AddCodePunkConsole();
    // Remove the default summarizer registration so GetService<ISessionSummarizer>() returns null
    builder.Services.RemoveAll<CodePunk.Core.Abstractions.ISessionSummarizer>();
    builder.Services.AddSingleton<IAnsiConsole>(sp => testConsole);

        var host = builder.Build();
        var root = RootCommandFactory.Create(host.Services);

        try
        {
            var code = await root.InvokeAsync(new[] { "plan", "create", "--from-session", "--session", "s1", "--json" });
            Assert.Equal(0, code);
            var text = testConsole.Output;
            text = System.Text.RegularExpressions.Regex.Replace(text, "\u001B\\[[0-9;]*[A-Za-z]", string.Empty);
            var doc = JsonDocument.Parse(text.Trim());
            var rootEl = doc.RootElement;
            Assert.Equal("plan.create.fromSession.v1", rootEl.GetProperty("schema").GetString());
            var error = rootEl.GetProperty("error");
            Assert.Equal("SummarizerUnavailable", error.GetProperty("code").GetString());
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public async Task PlanCreate_QuietMode_SuppressesDecorativeOutput_BeforeJson()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "codepunk-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", tmp);
        Environment.SetEnvironmentVariable("CODEPUNK_QUIET", "1");
        Directory.CreateDirectory(tmp);

        var testConsole = new TestConsole();
        testConsole.Profile.Width = 5000;
        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
        builder.Services.AddCodePunkServices(builder.Configuration);
        builder.Services.AddCodePunkConsole();
        builder.Services.AddSingleton<IAnsiConsole>(sp => testConsole);

        // register a fake summarizer so plan.create proceeds
        builder.Services.AddSingleton<CodePunk.Core.Abstractions.ISessionSummarizer>(new FakeSummarizer());

        var host = builder.Build();
        var root = RootCommandFactory.Create(host.Services);

        try
        {
            var code = await root.InvokeAsync(new[] { "plan", "create", "--from-session", "--session", "s1", "--json" });
            Assert.Equal(0, code);
            var outStr = testConsole.Output;
            var firstNonWhitespace = outStr.TrimStart();
            Assert.StartsWith("{", firstNonWhitespace);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } Environment.SetEnvironmentVariable("CODEPUNK_QUIET", null); }
    }

    private class FakeSummarizer : CodePunk.Core.Abstractions.ISessionSummarizer
    {
        public Task<CodePunk.Core.Models.SessionSummary?> SummarizeAsync(string sessionId, CodePunk.Core.Models.SessionSummaryOptions opts, CancellationToken ct = default)
        {
            var s = new CodePunk.Core.Models.SessionSummary { Goal = "Fake goal", CandidateFiles = new List<string> { "Program.cs" }, Rationale = "Fake", Truncated = false, UsedMessages = 3, TotalMessages = 3 };
            return Task.FromResult<CodePunk.Core.Models.SessionSummary?>(s);
        }
    }
}
