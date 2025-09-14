using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;
using CodePunk.Console.Configuration;
using CodePunk.Infrastructure.Configuration;
using CodePunk.Console.Stores;

namespace CodePunk.Console.Tests.Commands;

public class PlanBackwardCompatibilityTests
{
    [Fact]
    public async Task Plan_Without_Summary_Field_Deserializes_With_Null_Summary()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "codepunk-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", tmp);
        Directory.CreateDirectory(tmp);
        try
        {
            var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
            builder.Services.AddCodePunkServices(builder.Configuration);
            builder.Services.AddCodePunkConsole();
            var testConsole = new TestConsole();
            builder.Services.AddSingleton<IAnsiConsole>(sp => testConsole);
            var host = builder.Build();
            var store = host.Services.GetRequiredService<IPlanFileStore>();

            var id = await store.CreateAsync("Legacy goal");
            // Overwrite the stored json to mimic legacy file (remove Summary property entirely)
            var planPath = Path.Combine(tmp, "plans", id + ".json");
            var json = await File.ReadAllTextAsync(planPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            // root has Definition and Files; ensure we produce object without Summary
            var legacy = new
            {
                Definition = JsonSerializer.Deserialize<object>(root.GetProperty("Definition").GetRawText()),
                Files = new object[0]
            };
            var legacyJson = JsonSerializer.Serialize(legacy, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(planPath, legacyJson);

            var rec = await store.GetAsync(id);
            Assert.NotNull(rec);
            Assert.Null(rec!.Summary); // Should be null, not throw
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }
    }
}
