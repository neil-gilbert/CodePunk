using Spectre.Console;
using System.CommandLine;
using System.Linq;
namespace CodePunk.Console.Rendering;
internal static class HelpRenderer
{
    public static int ShowRootHelp(IAnsiConsole console, RootCommand root)
    {
    var width = console.Profile.Width;
    var narrow = width > 0 && width < 70; // only collapse in extremely narrow terminals
        RenderLogo(console, narrow);

        console.Write(new Rule("[bold yellow]Agentic Coding Assistant[/]").Centered());
        console.WriteLine();
        console.MarkupLine("[grey]Your tools, your code, your workflow, any model.[/]");
        console.WriteLine();

        var hasAnyApiKey = HasAnyApiKey();
        RenderQuickStart(console, narrow);
    console.MarkupLine("[cyan]First time? Run /setup to configure a provider & key; use /reload after adding keys if needed.[/]");
    console.WriteLine();
        RenderUsage(console);
        RenderCommands(console, root);
        if (!narrow)
        {
            RenderAllCommands(console, root);
            RenderExamples(console);
            RenderSlash(console);
            RenderEnv(console);
            RenderJson(console);
            RenderGlobalOptions(console, root);
            RenderCommandDetails(console, root, maxPerCommand: 6);
        }
        if (!hasAnyApiKey)
        {
            RenderNoKeyTip(console);
        }
        RenderFooter(console);
        return 0;
    }

    private static void RenderQuickStart(IAnsiConsole console, bool narrow)
    {
        console.MarkupLine("[bold underline]Quick Start[/]");
        var table = new Table().NoBorder();
        table.AddColumn("");
        table.AddColumn("");
        table.AddRow("API Key", "[green]export OPENAI_API_KEY=sk-...[/]");
        table.AddRow("Chat", "[green]codepunk run \"Explain this code\"[/]");
        table.AddRow("Interactive", "[green]codepunk[/]");
        if (!narrow)
            table.AddRow("Plan", "[green]codepunk plan create --goal 'Improve logging'[/]");
        console.Write(table);
        console.WriteLine();
    }

    private static void RenderCommands(IAnsiConsole console, RootCommand root)
    {
        console.MarkupLine("[bold underline]Top Commands[/]");
        var commandTable = new Table().Border(TableBorder.Rounded).Title("[teal]CLI[/]");
        commandTable.AddColumn("Command");
        commandTable.AddColumn("Description");
        var important = new (string cmd, string desc)[]
        {
            ("run <prompt>", "One-shot or interactive reply (omit <prompt> to open loop)"),
            ("auth login", "Store a provider API key"),
            ("models", "List models (add --json)"),
            ("sessions list", "Recent sessions"),
            ("plan create", "Start a change plan"),
            ("plan add", "Stage file modification/deletion"),
            ("plan apply", "Apply staged changes (drift-safe)")
        };
        foreach (var (c,d) in important)
        {
            commandTable.AddRow($"[green]{c}[/]", d);
        }
        console.Write(commandTable);
        console.WriteLine();
        console.MarkupLine("For full help on a command: [yellow]codepunk <command> --help[/]");
        console.WriteLine();
    }

    private static void RenderUsage(IAnsiConsole console)
    {
        console.MarkupLine("[bold underline]Usage[/]");
        var usage = new Table().NoBorder();
        usage.AddColumn(""); usage.AddColumn("");
        usage.AddRow("Interactive", "[green]codepunk[/]");
        usage.AddRow("One-shot", "[green]codepunk run \"Explain this file\"[/]");
        usage.AddRow("Session continue", "[green]codepunk run --continue[/]");
        usage.AddRow("Specify model", "[green]codepunk run --model anthropic/claude-3-5-sonnet[/]");
        usage.AddRow("JSON output", "[green]codepunk models --json[/]");
        usage.AddRow("Plan workflow", "[green]codepunk plan create --goal 'Refactor logging'[/]");
        console.Write(usage);
        console.WriteLine();
    }

    private static void RenderAllCommands(IAnsiConsole console, RootCommand root)
    {
        console.MarkupLine("[bold underline]All Top-Level Commands[/]");
        var t = new Table().Border(TableBorder.MinimalHeavyHead);
        t.AddColumn("Command");
        t.AddColumn("Description");
        foreach (var c in root.Children.OfType<Command>().Where(c => c.Name != root.Name))
        {
            t.AddRow($"[green]{c.Name}[/]", string.IsNullOrWhiteSpace(c.Description) ? "" : c.Description);
        }
        console.Write(t);
        console.WriteLine();
    }

    private static void RenderExamples(IAnsiConsole console)
    {
        console.MarkupLine("[bold underline]Examples[/]");
        var eg = new Table().NoBorder(); eg.AddColumn(""); eg.AddColumn("");
        eg.AddRow("List models", "[green]codepunk models --available-only[/]");
        eg.AddRow("Start plan", "[green]codepunk plan create --goal 'Improve error handling'[/]");
        eg.AddRow("Stage file", "[green]codepunk plan add --id <id> --path src/App.cs --after-file App.updated.cs[/]");
        eg.AddRow("Diff plan", "[green]codepunk plan diff --id <id>[/]");
        eg.AddRow("Apply plan", "[green]codepunk plan apply --id <id> --dry-run[/]");
        eg.AddRow("Show session", "[green]codepunk sessions show --id <sessionId> --json[/]");
        console.Write(eg);
        console.WriteLine();
    }

    private static void RenderSlash(IAnsiConsole console)
    {
        console.MarkupLine("[bold underline]Interactive Slash Commands[/]");
        var slash = new Table().Border(TableBorder.Rounded);
        slash.AddColumn("Command"); slash.AddColumn("Purpose");
        var rows = new (string, string)[]
        {
            ("/help", "Show interactive help"),
            ("/sessions", "List recent sessions"),
            ("/load <id>", "Load a previous session"),
            ("/setup", "Guided setup (provider + key)"),
            ("/reload", "Reload providers after adding keys"),
            ("/providers", "List providers & persistence paths"),
            ("/plan create --goal ...", "Create a plan"),
            ("/plan add --id --path", "Stage change in plan"),
            ("/plan diff --id", "View staged diffs"),
            ("/plan apply --id", "Apply plan changes"),
            ("/quit", "Exit interactive mode")
        };
        foreach (var (c,d) in rows) slash.AddRow($"[green]{c}[/]", d);
        console.Write(slash);
        console.WriteLine();
    }

    private static void RenderEnv(IAnsiConsole console)
    {
        console.MarkupLine("[bold underline]Environment Variables[/]");
        var env = new Table().Border(TableBorder.Rounded);
        env.AddColumn(new TableColumn("[bold]Variable[/]").Centered());
        env.AddColumn(new TableColumn("[bold]Purpose[/]").Centered());
        env.AddRow("[green]OPENAI_API_KEY[/]", "[white]Authenticate OpenAI models[/]");
        env.AddRow("[green]ANTHROPIC_API_KEY[/]", "[white]Authenticate Anthropic models[/]");
        env.AddRow("[green]CODEPUNK_VERBOSE=1[/]", "[white]Enable verbose logging + OTLP console exporter[/]");
        env.AddRow("[green]CODEPUNK_QUIET=1[/]", "[white]Suppress decorative output[/]");
        console.Write(env);
        console.WriteLine();
    }

    private static void RenderJson(IAnsiConsole console)
    {
        console.MarkupLine("[bold underline]JSON / Automation[/]");
        console.MarkupLine("Add [green]--json[/] to supported commands (e.g. [yellow]models[/], [yellow]plan diff[/]) to emit structured output with a [italic]schema[/] field.");
        console.WriteLine();
    }

    private static void RenderFooter(IAnsiConsole console)
    {
        console.Write(new Rule());
        console.MarkupLine("[grey]CodePunk v" + typeof(HelpRenderer).Assembly.GetName().Version + " — MIT Licensed[/]");
        console.MarkupLine("[grey]GitHub: https://github.com/neil-gilbert/CodePunk[/]");
    }

    private static void RenderGlobalOptions(IAnsiConsole console, RootCommand root)
    {
        console.MarkupLine("[bold underline]Global Options[/]");
        var t = new Table().Border(TableBorder.Rounded);
        t.AddColumn("Option"); t.AddColumn("Description");
        foreach (var opt in root.Options)
        {
            var alias = opt.Aliases.FirstOrDefault() ?? opt.Name;
            t.AddRow($"[green]{alias}[/]", opt.Description ?? "");
        }
        console.Write(t);
        console.WriteLine();
    }

    private static void RenderCommandDetails(IAnsiConsole console, RootCommand root, int maxPerCommand)
    {
        console.MarkupLine("[bold underline]Command Details[/]");
        foreach (var cmd in root.Children.OfType<Command>().Where(c => c.Name != root.Name))
        {
            var subTable = new Table().Border(TableBorder.Minimal).Title($"[yellow]{cmd.Name}[/]");
            subTable.AddColumn("Argument/Option");
            subTable.AddColumn("Description");
            int count = 0;
            foreach (var sym in cmd.Children)
            {
                if (sym is Option o)
                {
                    var alias = o.Aliases.FirstOrDefault() ?? o.Name;
                    subTable.AddRow($"[green]{alias}[/]", o.Description ?? "");
                    count++;
                }
                else if (sym is Argument a)
                {
                    subTable.AddRow($"[green]{a.Name}[/]", a.Description ?? "");
                    count++;
                }
                if (count >= maxPerCommand) break;
            }
            if (count == 0)
            {
                subTable.AddRow("(none)", "");
            }
            console.Write(subTable);
        }
        console.WriteLine();
    }

    private static bool HasAnyApiKey()
    {
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")) ||
               !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));
    }

    private static void RenderNoKeyTip(IAnsiConsole console)
    {
        var p = new Panel(new Markup("[yellow]No AI provider key detected.[/]\nSet [green]OPENAI_API_KEY[/] or run:\n[green]codepunk auth login --provider openai --key sk-...[/]"))
            .Header("Next Step")
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(Color.Yellow));
        console.Write(p);
        console.WriteLine();
    }

    private static void RenderLogo(IAnsiConsole console, bool narrow)
    {
        if (narrow)
        {
            var mini = new Markup("[mediumspringgreen]CodePunk[/]");
            console.Write(new Panel(mini).Collapse().Border(BoxBorder.Rounded).BorderStyle(new Style(Color.MediumSpringGreen)));
            return;
        }
        var lines = GradientFigletLines();
        var panelText = string.Join('\n', lines);
        console.Write(new Panel(new Markup(panelText))
            .Padding(0,0,0,0)
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(Color.MediumSpringGreen))
            .Header("CodePunk"));
    }

    private static string[] GradientFigletLines()
    {
        // Pre-rendered figlet for "CodePunk" (standard font)
        string[] raw = {
            "  ____               _          ____                    _     ",
            " / ___|   ___     __| |   ___  |  _ \\   _   _   _ __   | | __ ",
            "| |      / _ \\   / _` |  / _ \\ | |_) | | | | | | '_ \\  | |/ / ",
            "| |___  | (_) | | (_| | |  __/ |  __/  | |_| | | | | | | |   <  ",
            " \\____|  \\___/   \\__,_|  \\___| |_|      \\__,_| |_| |_| |_|\\_\\ "
        };
        var palette = new[]{ Color.Red1, Color.Orange1, Color.Yellow1, Color.Chartreuse1, Color.Cyan1, Color.MediumPurple, Color.MediumVioletRed };
        string ColorToken(Color c) => c.ToString().ToLowerInvariant();
        var output = new string[raw.Length];
        for (int li = 0; li < raw.Length; li++)
        {
            var line = raw[li];
            int len = line.Length;
            var sb = new System.Text.StringBuilder(len * 2);
            string? active = null;
            for (int i = 0; i < len; i++)
            {
                int idx = (int)Math.Round((double)i / Math.Max(1, len - 1) * (palette.Length - 1));
                var token = ColorToken(palette[idx]);
                if (active != token)
                {
                    if (active != null) sb.Append("[/]");
                    sb.Append('[').Append(token).Append(']');
                    active = token;
                }
                sb.Append(line[i]);
            }
            if (active != null) sb.Append("[/]");
            output[li] = sb.ToString();
        }
        return output;
    }
}
