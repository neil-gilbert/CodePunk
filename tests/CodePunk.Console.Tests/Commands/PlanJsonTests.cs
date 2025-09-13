using System.CommandLine;
using System.Text.Json;
using Spectre.Console.Testing;
using CodePunk.Console.Commands;
using CodePunk.Console.Stores;
using CodePunk.Infrastructure.Configuration;
using CodePunk.Console.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console;
using Xunit;

namespace CodePunk.Console.Tests.Commands;

public class PlanJsonTests
{
    private static JsonDocument ParseJson(TestConsole console) => JsonDocument.Parse(console.Output);
    private static JsonDocument ExtractJson(TestConsole console)
    {
        var text = console.Output;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) throw new InvalidOperationException($"Could not locate JSON braces. Output:\n{text}");
        var slice = text.Substring(start, end - start + 1);
        return JsonDocument.Parse(slice);
    }
    private static (RootCommand root, IServiceProvider sp, string tmp, TestConsole console) BuildWithConsole()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "codepunk-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", tmp);
        Directory.CreateDirectory(tmp);
        var testConsole = new TestConsole();
    // Prevent line wrapping that can insert raw newlines into JSON strings
    testConsole.Profile.Width = 5000;
        var builder = Host.CreateApplicationBuilder([]);
        builder.Services.AddCodePunkServices(builder.Configuration);
        builder.Services.AddCodePunkConsole();
        builder.Services.AddSingleton<IAnsiConsole>(sp => testConsole);
        var host = builder.Build();
        var root = RootCommandFactory.Create(host.Services);
        return (root, host.Services, tmp, testConsole);
    }

    private static async Task<string> CreateAsync(IPlanFileStore store)
    {
        return await store.CreateAsync("Goal");
    }

    [Fact]
    public async Task Plan_Add_Json_Success()
    {
        var (root, sp, tmp, testConsole) = BuildWithConsole();
        try
        {
            var store = sp.GetRequiredService<IPlanFileStore>();
            var id = await CreateAsync(store);
            var workDir = Path.Combine(tmp, "w"); Directory.CreateDirectory(workDir);
            var file = Path.Combine(workDir, "a.txt"); await File.WriteAllTextAsync(file, "Line1\n");
            var code = await root.InvokeAsync(["plan","add","--id",id,"--path",file,"--json"]);
            Assert.Equal(0, code);
            using var doc = ExtractJson(testConsole);
            var rootEl = doc.RootElement;
            Assert.Equal("plan.add.v1", rootEl.GetProperty("schema").GetString());
            Assert.Equal(id, rootEl.GetProperty("planId").GetString());
            Assert.True(rootEl.GetProperty("file").GetProperty("staged").GetBoolean());
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public async Task Plan_Add_Json_FileMissing()
    {
        var (root, sp, tmp, testConsole) = BuildWithConsole();
        try
        {
            var store = sp.GetRequiredService<IPlanFileStore>();
            var id = await CreateAsync(store);
            var code = await root.InvokeAsync(["plan","add","--id",id,"--path","missing.txt","--json"]);
            Assert.Equal(0, code);
            using var doc = ExtractJson(testConsole);
            var err = doc.RootElement.GetProperty("error");
            Assert.Equal("FileMissing", err.GetProperty("code").GetString());
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public async Task Plan_Apply_Json_DryRun()
    {
        var (root, sp, tmp, testConsole) = BuildWithConsole();
        try
        {
            var store = sp.GetRequiredService<IPlanFileStore>();
            var id = await CreateAsync(store);
            var workDir = Path.Combine(tmp, "w"); Directory.CreateDirectory(workDir);
            var file = Path.Combine(workDir, "b.txt"); await File.WriteAllTextAsync(file, "A\n");
            var after = Path.Combine(workDir, "b.new.txt"); await File.WriteAllTextAsync(after, "B\n");
            await root.InvokeAsync(["plan","add","--id",id,"--path",file,"--after-file",after]);
            var code = await root.InvokeAsync(["plan","apply","--id",id,"--dry-run","--json"]);
            Assert.Equal(0, code);
            using var doc = ExtractJson(testConsole);
            var summary = doc.RootElement.GetProperty("summary");
            Assert.Equal(1, summary.GetProperty("applied").GetInt32());
            Assert.True(doc.RootElement.GetProperty("dryRun").GetBoolean());
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public async Task Plan_Apply_Json_Drift()
    {
        var (root, sp, tmp, testConsole) = BuildWithConsole();
        try
        {
            var store = sp.GetRequiredService<IPlanFileStore>();
            var id = await CreateAsync(store);
            var workDir = Path.Combine(tmp, "w"); Directory.CreateDirectory(workDir);
            var file = Path.Combine(workDir, "c.txt"); await File.WriteAllTextAsync(file, "Original\n");
            var after = Path.Combine(workDir, "c.new.txt"); await File.WriteAllTextAsync(after, "Changed\n");
            await root.InvokeAsync(["plan","add","--id",id,"--path",file,"--after-file",after]);
            await File.WriteAllTextAsync(file, "Original mutated\n");
            var code = await root.InvokeAsync(["plan","apply","--id",id,"--json"]);
            Assert.Equal(0, code);
            using var doc = ExtractJson(testConsole);
            var summary = doc.RootElement.GetProperty("summary");
            Assert.Equal(0, summary.GetProperty("applied").GetInt32());
            Assert.True(summary.GetProperty("drift").GetInt32() >= 1);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }
}
