using CodePunk.Console.Themes;
using CodePunk.Core.Chat;
using CodePunk.Core.Checkpointing;
using Spectre.Console;

namespace CodePunk.Console.Commands;

public class CheckpointsCommand : ChatCommand
{
    private readonly ICheckpointService _checkpointService;

    public override string Name => "checkpoints";
    public override string Description => "List available file checkpoints";
    public override string[] Aliases => Array.Empty<string>();

    public CheckpointsCommand(ICheckpointService checkpointService)
    {
        _checkpointService = checkpointService;
    }

    public override async Task<CommandResult> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var console = AnsiConsole.Console;
        var limit = 20;

        if (args.Length > 0 && int.TryParse(args[0], out var parsedLimit))
        {
            limit = parsedLimit;
        }

        await _checkpointService.InitializeAsync(Directory.GetCurrentDirectory(), cancellationToken);

        console.WriteLine();
        console.Write(ConsoleStyles.HeaderRule("Available Checkpoints"));
        console.WriteLine();

        var result = await _checkpointService.ListCheckpointsAsync(limit, cancellationToken);

        if (!result.Success)
        {
            console.MarkupLine($"[red]Failed to list checkpoints: {result.ErrorMessage}[/]");
            console.WriteLine();
            return CommandResult.Ok();
        }

        if (result.Data == null || !result.Data.Any())
        {
            console.MarkupLine("[dim]No checkpoints available[/]");
            console.WriteLine();
            return CommandResult.Ok();
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn("ID")
            .AddColumn("Tool")
            .AddColumn("Description")
            .AddColumn("Created")
            .AddColumn("Files");

        foreach (var checkpoint in result.Data)
        {
            var shortId = checkpoint.Id.Length > 8 ? checkpoint.Id[..8] : checkpoint.Id;
            var description = checkpoint.Description.Length > 40
                ? checkpoint.Description[..37] + "..."
                : checkpoint.Description;

            table.AddRow(
                $"[cyan]{shortId}[/]",
                checkpoint.ToolName,
                description,
                checkpoint.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                checkpoint.ModifiedFiles.Count.ToString());
        }

        console.Write(table);
        console.WriteLine();
        console.MarkupLine($"[dim]Use /restore <id> to restore a checkpoint[/]");
        console.WriteLine();

        return CommandResult.Ok();
    }
}

public class RestoreCommand : ChatCommand
{
    private readonly ICheckpointService _checkpointService;

    public override string Name => "restore";
    public override string Description => "Restore files to a previous checkpoint state";
    public override string[] Aliases => Array.Empty<string>();

    public RestoreCommand(ICheckpointService checkpointService)
    {
        _checkpointService = checkpointService;
    }

    public override async Task<CommandResult> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var console = AnsiConsole.Console;

        console.WriteLine();

        if (args.Length == 0)
        {
            console.MarkupLine("[yellow]Usage: /restore <checkpoint_id>[/]");
            console.WriteLine();
            console.MarkupLine("[dim]Use /checkpoints to see available checkpoints[/]");
            console.WriteLine();
            return CommandResult.Ok();
        }

        await _checkpointService.InitializeAsync(Directory.GetCurrentDirectory(), cancellationToken);

        var checkpointId = args[0];

        console.MarkupLine($"[yellow]Restoring checkpoint: {checkpointId}[/]");

        var metadataResult = await _checkpointService.GetCheckpointAsync(checkpointId, cancellationToken);
        if (!metadataResult.Success)
        {
            console.WriteLine();
            console.MarkupLine($"[red]Checkpoint not found: {checkpointId}[/]");
            console.WriteLine();
            console.MarkupLine("[dim]Use /checkpoints to see available checkpoints[/]");
            console.WriteLine();
            return CommandResult.Ok();
        }

        var result = await _checkpointService.RestoreCheckpointAsync(checkpointId, cancellationToken);

        console.WriteLine();

        if (result.Success)
        {
            var metadata = metadataResult.Data!;
            console.MarkupLine(ConsoleStyles.Success("Checkpoint restored successfully"));
            console.WriteLine();
            console.MarkupLine($"[dim]Tool:[/] {metadata.ToolName}");
            console.MarkupLine($"[dim]Description:[/] {metadata.Description}");
            console.MarkupLine($"[dim]Files restored:[/] {metadata.ModifiedFiles.Count}");
        }
        else
        {
            console.MarkupLine($"[red]Failed to restore: {result.ErrorMessage}[/]");
        }

        console.WriteLine();

        return CommandResult.Ok();
    }
}
