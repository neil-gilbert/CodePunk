using Spectre.Console;
using System.CommandLine;
using System.Linq;
namespace CodePunk.Console.Rendering;
internal static class HelpRenderer
{
    public static int ShowRootHelp(IAnsiConsole console, RootCommand root)
    {
        var figlet = new FigletText("CodePunk").Color(Color.MediumSpringGreen);
        console.Write(new Panel(new Align(figlet, HorizontalAlignment.Left))
            .Padding(0,0,0,0)
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(Color.MediumSpringGreen))
            .Header("CodePunk"));

        console.Write(new Rule("[bold yellow]Agentic Coding Assistant[/]").Centered());
        console.WriteLine();
        console.MarkupLine("[grey]Your tools, your code, your workflow, any model.[/]");
        console.WriteLine();

        RenderQuickStart(console);
        RenderUsage(console);
        RenderCommands(console, root);
        RenderAllCommands(console, root);
        RenderExamples(console);
        RenderSlash(console);
        RenderEnv(console);
        RenderJson(console);
        RenderFooter(console);
        return 0;
    }

    private static void RenderQuickStart(IAnsiConsole console)
    {
        console.MarkupLine("[bold underline]Quick Start[/]");
        var table = new Table().NoBorder();
        table.AddColumn("");
        table.AddColumn("");
        table.AddRow("API Key", "[green]export OPENAI_API_KEY=sk-...[/]");
        table.AddRow("Chat", "[green]codepunk run \"Explain this code\"[/]");
        table.AddRow("Interactive", "[green]codepunk[/]");
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
        var env = new Table().Border(TableBorder.None);
        env.AddColumn("Variable");
        env.AddColumn("Purpose");
        env.AddRow("OPENAI_API_KEY", "Authenticate OpenAI models");
        env.AddRow("ANTHROPIC_API_KEY", "Authenticate Anthropic models");
        env.AddRow("CODEPUNK_VERBOSE=1", "Enable verbose logging + OTLP console exporter");
        env.AddRow("CODEPUNK_QUIET=1", "Suppress decorative output");
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
}
