using System.CommandLine;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodePunk.Console.Commands;
using CodePunk.Core.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CodePunk.Console.Configuration;
using Microsoft.Extensions.Configuration;
using CodePunk.Console.Tests.Testing;

namespace CodePunk.Console.Tests.Commands;

/// <summary>
/// Base helper for console command tests providing host setup, command invocation,
/// JSON extraction utilities, and service override capability.
/// </summary>
public abstract class ConsoleTestBase
{
    private ConsoleTestHostContext _ctx = ConsoleTestHostFactory.Create();
    private readonly List<Action<IServiceCollection>> _pendingServiceOverrides = new();

    protected void WithServices(Action<IServiceCollection> configure)
    {
        _pendingServiceOverrides.Add(configure);
    }

    private void RebuildHostIfNeeded()
    {
        if (_pendingServiceOverrides.Count == 0) return;
        var overrides = _pendingServiceOverrides.ToList();
        _pendingServiceOverrides.Clear();
        var sessionStore = _ctx.SessionStore;
        var agentStore = _ctx.AgentStore;
        var llm = _ctx.LlmService;
        var tool = _ctx.ToolService;
        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
        builder.Services.AddLogging();
        builder.Services.AddCodePunkConsole();
        builder.Services.AddSingleton(sessionStore.Object);
        builder.Services.AddSingleton(agentStore.Object);
        builder.Services.AddSingleton(llm.Object);
        builder.Services.AddSingleton(tool.Object);
        foreach (var act in overrides) act(builder.Services);
        // If any test override registered an IConfiguration, bind PlanAI options from it so tests can override settings like MaxFiles.
        try
        {
            using var tempSp = builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = false });
            var cfg = tempSp.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
            if (cfg != null)
            {
                builder.Services.Configure<CodePunk.Console.Planning.PlanAiGenerationOptions>(cfg.GetSection("PlanAI"));
            }
        }
        catch
        {
            // best-effort only for tests
        }
        _ctx = new ConsoleTestHostContext(builder.Build(), sessionStore, agentStore, _ctx.SessionService, _ctx.MessageService, llm, tool);
    }

    protected string Run(string args)
    {
        RebuildHostIfNeeded();
        var root = RootCommandFactory.Create(_ctx.Host.Services);
        using var sw = new StringWriter();
    var origOut = System.Console.Out;
    System.Console.SetOut(sw);
        try
        {
            var split = SimpleSplit(args).ToArray();
            var exit = root.Invoke(split);
            exit.Should().Be(0, "command should succeed for test scenario");
            return sw.ToString();
        }
        finally
        {
            System.Console.SetOut(origOut);
        }
    }

    protected JsonElement JsonLast(string consoleOutput)
    {
        var text = consoleOutput ?? string.Empty;
        // Strip ANSI escape sequences if any
        text = Regex.Replace(text, "\u001b\\[[0-9;]*m", string.Empty);

        // Scan the text for balanced JSON objects { ... } and record their positions
        var candidates = new List<(int start, int end)>();
        int depth = 0;
        int startPos = -1;
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '{')
            {
                if (depth == 0) startPos = i;
                depth++;
            }
            else if (c == '}')
            {
                if (depth > 0) depth--;
                if (depth == 0 && startPos >= 0)
                {
                    candidates.Add((startPos, i));
                    startPos = -1;
                }
            }
        }

        // Prefer the most recent JSON objects (by end position), try newest first.
        var ordered = candidates.OrderByDescending(c => c.end).ThenByDescending(c => c.start).ToList();
        foreach (var (s, e) in ordered)
        {
            if (e <= s) continue;
            var slice = text.Substring(s, e - s + 1);
            try
            {
                using var doc = JsonDocument.Parse(slice);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    // Only return objects that look like top-level payloads: contain schema, error, or files
                    if (root.TryGetProperty("schema", out _) || root.TryGetProperty("error", out _) || root.TryGetProperty("files", out _))
                    {
                        return root.Clone();
                    }
                }
                // otherwise keep searching
            }
            catch (JsonException)
            {
                // invalid JSON, try next candidate
            }
        }

        throw new InvalidOperationException("No JSON object found in output");
    }
    private static IEnumerable<string> SimpleSplit(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) yield break;
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < commandLine.Length; i++)
        {
            var c = commandLine[i];
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
            }
            else current.Append(c);
        }
        if (current.Length > 0) yield return current.ToString();
    }
}
