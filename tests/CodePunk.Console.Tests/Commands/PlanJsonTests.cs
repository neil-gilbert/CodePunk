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
        // strip ANSI escape sequences
        text = System.Text.RegularExpressions.Regex.Replace(text, "\u001B\\[[0-9;]*[A-Za-z]", string.Empty);
        // Some tests may have multiple JSON objects if prior commands wrote JSON.
        // We capture the last complete JSON object by scanning tokens.
        int depth = 0; int lastObjStart = -1; int lastObjEnd = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                if (depth == 0) lastObjStart = i;
                depth++;
            }
            else if (text[i] == '}')
            {
                if (depth > 0) depth--;
                if (depth == 0 && lastObjStart >= 0)
                {
                    lastObjEnd = i;
                }
            }
        }
        if (lastObjStart < 0 || lastObjEnd < lastObjStart) throw new InvalidOperationException($"Could not locate JSON braces. Output:\n{text}");
        var slice = text.Substring(lastObjStart, lastObjEnd - lastObjStart + 1);
        return JsonDocument.Parse(slice);
    }
    
    private static (RootCommand root, IServiceProvider sp, string tmp, TestConsole console) BuildWithConsole()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "codepunk-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", tmp);
        Directory.CreateDirectory(tmp);
        
        var testConsole = new TestConsole();
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

    [Fact]
    public async Task Plan_Add_Delete_And_Apply_DryRun()
    {
        var (root, sp, tmp, testConsole) = BuildWithConsole();
        try
        {
            var store = sp.GetRequiredService<IPlanFileStore>();
            var id = await CreateAsync(store);
            var workDir = Path.Combine(tmp, "w"); Directory.CreateDirectory(workDir);
            var file = Path.Combine(workDir, "d.txt"); await File.WriteAllTextAsync(file, "Delete me\n");
            await root.InvokeAsync(["plan","add","--id",id,"--path",file,"--delete","--json"]);
            testConsole.Clear();
            var code = await root.InvokeAsync(["plan","apply","--id",id,"--dry-run","--json"]);
            Assert.Equal(0, code);
            using var doc = ExtractJson(testConsole);
            var changes = doc.RootElement.GetProperty("changes");
            Assert.Contains(changes.EnumerateArray(), el => el.GetProperty("action").GetString()!.Contains("dry-run-delete"));
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public async Task Plan_List_Show_Diff_Json_Schemas()
    {
        var (root, sp, tmp, testConsole) = BuildWithConsole();
        try
        {
            var store = sp.GetRequiredService<IPlanFileStore>();
            var id = await CreateAsync(store);
            var workDir = Path.Combine(tmp, "w"); Directory.CreateDirectory(workDir);
            var file = Path.Combine(workDir, "e.txt"); await File.WriteAllTextAsync(file, "V1\n");
            var after = Path.Combine(workDir, "e.after.txt"); await File.WriteAllTextAsync(after, "V2\n");
            await root.InvokeAsync(["plan","add","--id",id,"--path",file,"--after-file",after]);

            // list
            testConsole.Clear();
            await root.InvokeAsync(["plan","list","--json"]);
            using (var docList = ExtractJson(testConsole))
            {
                Assert.Equal("plan.list.v1", docList.RootElement.GetProperty("schema").GetString());
            }

            // show
            testConsole.Clear();
            await root.InvokeAsync(["plan","show","--id",id,"--json"]);
            using (var docShow = ExtractJson(testConsole))
            {
                Assert.Equal("plan.show.v1", docShow.RootElement.GetProperty("schema").GetString());
            }

            // diff
            testConsole.Clear();
            await root.InvokeAsync(["plan","diff","--id",id,"--json"]);
            using (var docDiff = ExtractJson(testConsole))
            {
                Assert.Equal("plan.diff.v1", docDiff.RootElement.GetProperty("schema").GetString());
                Assert.Equal(id, docDiff.RootElement.GetProperty("planId").GetString());
            }
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }
}

public class HelpIncludesPlanTests
{
    [Fact]
    public void Help_Should_List_Plan_Command()
    {
        var builder = Host.CreateApplicationBuilder([]);
        builder.Services.AddCodePunkServices(builder.Configuration);
        builder.Services.AddCodePunkConsole();
        
        var host = builder.Build();
        var processor = host.Services.GetRequiredService<CommandProcessor>();
        var help = processor.GetAllCommands().OfType<HelpCommand>().First();
        var console = AnsiConsole.Console;
        var hasPlan = processor.GetAllCommands().Any(c => c.Name == "plan");
        
        Assert.True(hasPlan, "/plan command not registered");
    }
}
