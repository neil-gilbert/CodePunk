using CodePunk.Core.Abstractions;
using CodePunk.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Spectre.Console;

var builder = Host.CreateApplicationBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Services.AddSerilog();

// Add CodePunk services
builder.Services.AddCodePunkServices(builder.Configuration);

// Add Spectre.Console
builder.Services.AddSingleton<IAnsiConsole>(AnsiConsole.Console);

var host = builder.Build();

// Ensure database is created
await host.Services.EnsureDatabaseCreatedAsync();

// Run the application
var console = host.Services.GetRequiredService<IAnsiConsole>();

// Display welcome message
console.Clear();
console.Write(
    new FigletText("CodePunk.NET")
        .Centered()
        .Color(Color.Cyan1));

console.MarkupLine("[dim]AI-powered coding assistant - Phase 1 Foundation[/]");
console.WriteLine();

var sessionService = host.Services.GetRequiredService<ISessionService>();

try
{
    // Test basic functionality
    console.MarkupLine("[yellow]Testing Phase 1 implementation...[/]");
    
    var session = await sessionService.CreateAsync("Phase 1 Test Session");
    console.MarkupLine($"✓ Created session: [green]{session.Id}[/] - {session.Title}");

    var sessions = await sessionService.GetRecentAsync(10);
    console.MarkupLine($"✓ Found [yellow]{sessions.Count}[/] recent sessions");

    // Display session details in a table
    if (sessions.Count > 0)
    {
        console.WriteLine();
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn("[bold]ID[/]")
            .AddColumn("[bold]Title[/]")
            .AddColumn("[bold]Created[/]")
            .AddColumn("[bold]Messages[/]");

        foreach (var s in sessions.Take(5))
        {
            table.AddRow(
                s.Id[..8] + "...",
                s.Title,
                s.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                s.MessageCount.ToString()
            );
        }

        console.Write(table);
    }

    console.WriteLine();
    console.MarkupLine("[green]Phase 1 foundation is working correctly![/]");
    console.MarkupLine("[dim]Press any key to exit...[/]");
    Console.ReadKey();
}
catch (Exception ex)
{
    console.WriteException(ex);
    Environment.Exit(1);
}

await host.StopAsync();
