using Civ6Async.Cli.Commands;
using Civ6Async.Cli.Commands.Game;
using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("civ6-async");

    // Mod-management commands.
    config.AddCommand<InstallCommand>("install")
        .WithDescription("Install or update the civ6-async mod into Civilization VI's Mods folder.");
    config.AddCommand<UninstallCommand>("uninstall")
        .WithDescription("Remove the civ6-async mod from Civilization VI's Mods folder.");
    config.AddCommand<StatusCommand>("status")
        .WithDescription("Show whether the mod is installed and where.");
    config.AddCommand<HealthCommand>("health")
        .WithDescription("Run a full set of checks against the install.");

    // Shared-game coordination commands.
    config.AddBranch("game", branch =>
    {
        branch.SetDescription("Coordinate hotseat games across players via a shared folder.");
        branch.AddCommand<GameInitCommand>("init")
            .WithDescription("Create a new game in a shared folder.");
        branch.AddCommand<GameJoinCommand>("join")
            .WithDescription("Join an existing game in a shared folder.");
        branch.AddCommand<GameStatusCommand>("status")
            .WithDescription("Show whose turn it is in the active game.");
        branch.AddCommand<GameCheckCommand>("check")
            .WithDescription("If it's your turn, download the latest save into your Civ saves folder.");
        branch.AddCommand<GameSubmitCommand>("submit")
            .WithDescription("Upload the save you just played, advancing the turn.");
        branch.AddCommand<GameWatchCommand>("watch")
            .WithDescription("Run in the background, notifying you when it's your turn or you've saved.");
        branch.AddCommand<GameWebhookCommand>("webhook")
            .WithDescription("Set / clear / show the Discord webhook URL for this game's submit pings.");
    });
});

// No args -> interactive menu (user double-clicked).
// Any args  -> normal CLI mode.
if (args.Length == 0)
    return await RunInteractiveAsync(app);

return await app.RunAsync(args);


static async Task<int> RunInteractiveAsync(CommandApp app)
{
    DrawBanner();

    while (true)
    {
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<MenuChoice>()
                .Title("What do you want to do?")
                .UseConverter(c => c.Label)
                .AddChoices(
                    new MenuChoice("Game: check whose turn",     new[] { "game", "status" }),
                    new MenuChoice("Game: download latest save", new[] { "game", "check" }),
                    new MenuChoice("Game: submit my turn",       new[] { "game", "submit" }),
                    new MenuChoice("Game: watch (background)",   new[] { "game", "watch" }),
                    new MenuChoice("Mod: install / update",      new[] { "install" }),
                    new MenuChoice("Mod: uninstall",             new[] { "uninstall" }),
                    new MenuChoice("Mod: status",                new[] { "status" }),
                    new MenuChoice("Mod: health check",          new[] { "health" }),
                    new MenuChoice("Exit",                       null)));

        if (choice.Args is null) return 0;

        AnsiConsole.WriteLine();
        await app.RunAsync(choice.Args);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Press any key to return to the menu...[/]");
        Console.ReadKey(intercept: true);
        AnsiConsole.Clear();
        DrawBanner();
    }
}

static void DrawBanner()
{
    AnsiConsole.Write(new FigletText("civ6-async").Color(Color.Aqua));
    AnsiConsole.MarkupLine("[grey]Civilization VI hotseat helper[/]");
    AnsiConsole.WriteLine();
}

internal sealed record MenuChoice(string Label, string[]? Args);
