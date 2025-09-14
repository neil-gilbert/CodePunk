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

namespace CodePunk.Console.Tests.Commands;

public class PlanCreateFromSession_SummaryUnavailableTest
{
    [Fact]
    public async Task PlanCreate_FromSession_SummarizerReturnsNull_EmitsSummaryUnavailableJson()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "codepunk-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", tmp);
        Directory.CreateDirectory(tmp);

        var testConsole = new TestConsole();
        testConsole.Profile.Width = 5000;
        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
        builder.Services.AddCodePunkServices(builder.Configuration);
        builder.Services.AddCodePunkConsole();
        // register a summarizer that returns null
        builder.Services.AddSingleton<CodePunk.Core.Abstractions.ISessionSummarizer>(new NullSummarizer());
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
            Assert.Equal("SummaryUnavailable", error.GetProperty("code").GetString());
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    private class NullSummarizer : CodePunk.Core.Abstractions.ISessionSummarizer
    {
        public Task<CodePunk.Core.Models.SessionSummary?> SummarizeAsync(string sessionId, CodePunk.Core.Models.SessionSummaryOptions opts, CancellationToken ct = default)
        {
            return Task.FromResult<CodePunk.Core.Models.SessionSummary?>(null);
        }
    }
}
