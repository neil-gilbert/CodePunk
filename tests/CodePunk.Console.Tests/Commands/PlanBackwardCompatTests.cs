using System.Text.Json;
using Xunit;
using System.IO;
using System.Threading.Tasks;
using CodePunk.Console.Stores;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using CodePunk.Console.Commands;
using Spectre.Console;
using Spectre.Console.Testing;
using CodePunk.Console.Configuration;
using CodePunk.Infrastructure.Configuration;

namespace CodePunk.Console.Tests.Commands;

public class PlanBackwardCompatTests
{
    [Fact]
    public async Task Plan_Load_OldFormat_Without_Summary_Succeeds()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "codepunk-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", tmp);
        Directory.CreateDirectory(tmp);
        var plansDir = Path.Combine(tmp, "plans");
        Directory.CreateDirectory(plansDir);
        // craft an old style plan file (no Summary property)
        var planId = "20240101010101-legacy";
        var legacyJson = "{" +
            "\n  \"Definition\": {\n    \"Id\": \"" + planId + "\",\n    \"Goal\": \"Legacy goal\",\n    \"CreatedUtc\": \"2024-01-01T01:01:01Z\"\n  }," +
            "\n  \"Files\": []\n}";
        await File.WriteAllTextAsync(Path.Combine(plansDir, planId + ".json"), legacyJson);

        var testConsole = new TestConsole();
        testConsole.Profile.Width = 5000;
        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
        builder.Services.AddCodePunkServices(builder.Configuration);
        builder.Services.AddCodePunkConsole();
        builder.Services.AddSingleton<IAnsiConsole>(sp => testConsole);
        var host = builder.Build();
        var store = host.Services.GetRequiredService<IPlanFileStore>();

        try
        {
            var rec = await store.GetAsync(planId);
            Assert.NotNull(rec);
            Assert.Equal(planId, rec!.Definition.Id);
            Assert.Equal("Legacy goal", rec.Definition.Goal);
            Assert.Null(rec.Summary); // backward compat: Summary absent => null
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }
}
