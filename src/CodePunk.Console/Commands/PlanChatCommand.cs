using System.CommandLine;
using CodePunk.Core.Abstractions;

namespace CodePunk.Console.Commands;

public class PlanChatCommand : ChatCommand
{
    private readonly IServiceProvider _services;
    private RootCommand? _root;
    private RootCommand Root => _root ??= RootCommandFactory.Create(_services);

    public PlanChatCommand(IServiceProvider services)
    {
        _services = services;
    }

    public override string Name => "plan";
    public override string Description => "Manage change plans: /plan create | add | diff | apply";
    public override string[] Aliases => Array.Empty<string>();

    public override async Task<CommandResult> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0 || (args.Length == 1 && (args[0].Equals("help", StringComparison.OrdinalIgnoreCase) || args[0] == "-h" || args[0] == "--help")))
        {
            var helpArgs = new[] { "plan", "--help" };
            await Root.InvokeAsync(helpArgs);
            return CommandResult.Ok();
        }
        var cmdArgs = new List<string> { "plan" };
        cmdArgs.AddRange(args);
        try
        {
            var exit = await Root.InvokeAsync(cmdArgs.ToArray());
            if (exit != 0)
            {
                return CommandResult.Error("plan command failed");
            }
            return CommandResult.Ok();
        }
        catch (Exception ex)
        {
            return CommandResult.Error("plan command error: " + ex.Message);
        }
    }
}
