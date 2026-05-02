using Civ6Async.Cli.Commands;
using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("civ6-async");

    config.AddCommand<InstallCommand>("install")
        .WithDescription("Install or update the civ6-async mod into Civilization VI's Mods folder.");

    config.AddCommand<UninstallCommand>("uninstall")
        .WithDescription("Remove the civ6-async mod from Civilization VI's Mods folder.");

    config.AddCommand<StatusCommand>("status")
        .WithDescription("Show whether the mod is installed and where.");

    config.AddCommand<HealthCommand>("health")
        .WithDescription("Run a full set of checks against the install.");
});

// No args -> interactive menu mode (user double-clicked the .exe).
// Any args -> normal CLI mode (returns when the command finishes).
if (args.Length == 0)
{
    return await RunInteractiveAsync(app);
}

return await app.RunAsync(args);


static async Task<int> RunInteractiveAsync(CommandApp app)
{
    AnsiConsole.Write(new FigletText("civ6-async").Color(Color.Aqua));
    AnsiConsole.MarkupLine("[grey]Civilization VI hotseat helper[/]");
    AnsiConsole.WriteLine();

    while (true)
    {
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<MenuChoice>()
                .Title("What do you want to do?")
                .UseConverter(c => c.Label)
                .AddChoices(
                    new MenuChoice("Install / update mod", new[] { "install" }),
                    new MenuChoice("Uninstall mod",        new[] { "uninstall" }),
                    new MenuChoice("Show status",          new[] { "status" }),
                    new MenuChoice("Health check",         new[] { "health" }),
                    new MenuChoice("Exit",                 null)));

        if (choice.Args is null) return 0;

        AnsiConsole.WriteLine();
        await app.RunAsync(choice.Args);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Press any key to return to the menu...[/]");
        Console.ReadKey(intercept: true);
        AnsiConsole.Clear();
        AnsiConsole.Write(new FigletText("civ6-async").Color(Color.Aqua));
        AnsiConsole.WriteLine();
    }
}

internal sealed record MenuChoice(string Label, string[]? Args);
