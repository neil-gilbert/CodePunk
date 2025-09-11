using System.CommandLine;
using CodePunk.Console.Commands;
using CodePunk.Console.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CodePunk.Console.Tests.Commands;

public class PlanApplyTests
{
    private static (RootCommand root, IServiceProvider sp, string tmp) Build()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "codepunk-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CODEPUNK_CONFIG_HOME", tmp);
        Directory.CreateDirectory(tmp);
        var builder = Host.CreateApplicationBuilder([]);
        builder.Services.AddCodePunkServices(builder.Configuration);
        builder.Services.AddCodePunkConsole();
        var host = builder.Build();
        return (RootCommandFactory.Create(host.Services), host.Services, tmp);
    }

    private static async Task<string> CreatePlanAsync(RootCommand root, IPlanFileStore store, string goal)
    {
        var code = await root.InvokeAsync(["plan", "create", "--goal", goal]);
        Assert.Equal(0, code);
        var defs = await store.ListAsync(1);
        Assert.Single(defs);
        return defs[0].Id;
    }

    [Fact]
    public async Task Plan_Add_File_With_After_Generates_Diff()
    {
        var (root, sp, tmp) = Build();
        try
        {
            var store = sp.GetRequiredService<IPlanFileStore>();
            var planId = await CreatePlanAsync(root, store, "Goal");
            var workDir = Path.Combine(tmp, "work"); Directory.CreateDirectory(workDir);
            var filePath = Path.Combine(workDir, "sample.txt");
            await File.WriteAllTextAsync(filePath, "Line1\nLine2\n");
            var afterPath = Path.Combine(workDir, "sample.after.txt");
            await File.WriteAllTextAsync(afterPath, "Line1\nLine2 changed\nLine3\n");
            var code = await root.InvokeAsync(["plan", "add", "--id", planId, "--path", filePath, "--after-file", afterPath, "--rationale", "Test change"]);
            Assert.Equal(0, code);
            var rec = await store.GetAsync(planId);
            Assert.NotNull(rec);
            Assert.Single(rec!.Files);
            var change = rec.Files[0];
            Assert.Contains("Line2", change.BeforeContent);
            Assert.NotNull(change.Diff);
            Assert.Contains("-Line2", change.Diff);
            Assert.Contains("+Line2 changed", change.Diff);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public async Task Plan_Apply_DryRun_Does_Not_Modify_File()
    {
        var (root, sp, tmp) = Build();
        try
        {
            var store = sp.GetRequiredService<IPlanFileStore>();
            var planId = await CreatePlanAsync(root, store, "Goal");
            var workDir = Path.Combine(tmp, "work"); Directory.CreateDirectory(workDir);
            var filePath = Path.Combine(workDir, "sample.txt");
            await File.WriteAllTextAsync(filePath, "A\n");
            var afterPath = Path.Combine(workDir, "sample.after.txt");
            await File.WriteAllTextAsync(afterPath, "B\n");
            await root.InvokeAsync(["plan", "add", "--id", planId, "--path", filePath, "--after-file", afterPath]);
            var code = await root.InvokeAsync(["plan", "apply", "--id", planId, "--dry-run"]);
            Assert.Equal(0, code);
            var current = await File.ReadAllTextAsync(filePath);
            Assert.Equal("A\n", current);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public async Task Plan_Apply_Drift_Detected_Blocks_Without_Force()
    {
        var (root, sp, tmp) = Build();
        try
        {
            var store = sp.GetRequiredService<IPlanFileStore>();
            var planId = await CreatePlanAsync(root, store, "Goal");
            var workDir = Path.Combine(tmp, "work"); Directory.CreateDirectory(workDir);
            var filePath = Path.Combine(workDir, "sample.txt");
            await File.WriteAllTextAsync(filePath, "Original\n");
            var afterPath = Path.Combine(workDir, "sample.after.txt");
            await File.WriteAllTextAsync(afterPath, "Modified\n");
            await root.InvokeAsync(["plan", "add", "--id", planId, "--path", filePath, "--after-file", afterPath]);
            await File.WriteAllTextAsync(filePath, "Original mutated\n"); // introduce drift
            var code = await root.InvokeAsync(["plan", "apply", "--id", planId]);
            Assert.Equal(0, code);
            var current = await File.ReadAllTextAsync(filePath);
            Assert.Equal("Original mutated\n", current); // unchanged due to drift
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public async Task Plan_Apply_Force_Overrides_Drift()
    {
        var (root, sp, tmp) = Build();
        try
        {
            var store = sp.GetRequiredService<IPlanFileStore>();
            var planId = await CreatePlanAsync(root, store, "Goal");
            var workDir = Path.Combine(tmp, "work"); Directory.CreateDirectory(workDir);
            var filePath = Path.Combine(workDir, "sample.txt");
            await File.WriteAllTextAsync(filePath, "Original\n");
            var afterPath = Path.Combine(workDir, "sample.after.txt");
            await File.WriteAllTextAsync(afterPath, "Modified\n");
            await root.InvokeAsync(["plan", "add", "--id", planId, "--path", filePath, "--after-file", afterPath]);
            await File.WriteAllTextAsync(filePath, "Original mutated\n"); // drift
            var code = await root.InvokeAsync(["plan", "apply", "--id", planId, "--force"]);
            Assert.Equal(0, code);
            var current = await File.ReadAllTextAsync(filePath);
            Assert.Equal("Modified\n", current); // overridden
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public async Task Plan_Apply_Creates_Backup_When_Modifying()
    {
        var (root, sp, tmp) = Build();
        try
        {
            var store = sp.GetRequiredService<IPlanFileStore>();
            var planId = await CreatePlanAsync(root, store, "Goal");
            var workDir = Path.Combine(tmp, "work"); Directory.CreateDirectory(workDir);
            var filePath = Path.Combine(workDir, "sample.txt");
            await File.WriteAllTextAsync(filePath, "Original\n");
            var afterPath = Path.Combine(workDir, "sample.after.txt");
            await File.WriteAllTextAsync(afterPath, "Modified\n");
            await root.InvokeAsync(["plan", "add", "--id", planId, "--path", filePath, "--after-file", afterPath]);
            var code = await root.InvokeAsync(["plan", "apply", "--id", planId]);
            Assert.Equal(0, code);
            // backup directory should exist under config home plans/backups
            var configHome = Environment.GetEnvironmentVariable("CODEPUNK_CONFIG_HOME")!;
            var backupsDir = Path.Combine(configHome, "plans", "backups");
            Assert.True(Directory.Exists(backupsDir));
            // find a subdirectory containing sample.txt
            var hit = Directory.GetDirectories(backupsDir).FirstOrDefault(d => File.Exists(Path.Combine(d, Path.GetFileName(filePath))));
            Assert.NotNull(hit);
            var backedUp = await File.ReadAllTextAsync(Path.Combine(hit!, Path.GetFileName(filePath)));
            Assert.Equal("Original\n", backedUp);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public async Task Plan_Apply_DryRun_Does_Not_Create_Backup()
    {
        var (root, sp, tmp) = Build();
        try
        {
            var store = sp.GetRequiredService<IPlanFileStore>();
            var planId = await CreatePlanAsync(root, store, "Goal");
            var workDir = Path.Combine(tmp, "work"); Directory.CreateDirectory(workDir);
            var filePath = Path.Combine(workDir, "sample.txt");
            await File.WriteAllTextAsync(filePath, "Orig\n");
            var afterPath = Path.Combine(workDir, "sample.after.txt");
            await File.WriteAllTextAsync(afterPath, "New\n");
            await root.InvokeAsync(["plan", "add", "--id", planId, "--path", filePath, "--after-file", afterPath]);
            var code = await root.InvokeAsync(["plan", "apply", "--id", planId, "--dry-run"]);
            Assert.Equal(0, code);
            var configHome = Environment.GetEnvironmentVariable("CODEPUNK_CONFIG_HOME")!;
            var backupsDir = Path.Combine(configHome, "plans", "backups");
            // backups dir may exist from EnsureCreated but should have no timestamp subdirs
            if (Directory.Exists(backupsDir))
            {
                var subdirs = Directory.GetDirectories(backupsDir);
                Assert.True(subdirs.Length == 0, "Dry run should not create backup subdirectories");
            }
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public async Task Plan_Apply_Skips_When_No_After()
    {
        var (root, sp, tmp) = Build();
        try
        {
            var store = sp.GetRequiredService<IPlanFileStore>();
            var planId = await CreatePlanAsync(root, store, "Goal");
            var workDir = Path.Combine(tmp, "work"); Directory.CreateDirectory(workDir);
            var filePath = Path.Combine(workDir, "sample.txt");
            await File.WriteAllTextAsync(filePath, "Line\n");
            await root.InvokeAsync(["plan", "add", "--id", planId, "--path", filePath]); // before only
            var code = await root.InvokeAsync(["plan", "apply", "--id", planId]);
            Assert.Equal(0, code);
            var current = await File.ReadAllTextAsync(filePath);
            Assert.Equal("Line\n", current);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }
}
