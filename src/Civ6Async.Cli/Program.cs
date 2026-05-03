using Civ6Async.Cli.Commands;
using Civ6Async.Cli.Commands.Game;
using Civ6Async.Cli.Services;
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
        .WithDescription("Check the mod install: Civ folder, writable, present, file integrity.");
    config.AddCommand<ResetCommand>("reset")
        .WithDescription("Wipe local helper state (config + downloaded turn saves). Cloud folders untouched.");
    config.AddCommand<DefaultsCommand>("defaults")
        .WithDescription("View and edit the storage defaults (Dropbox root, local folder root) the wizard pre-fills.");

    // Shared-game coordination commands.
    config.AddBranch("game", branch =>
    {
        branch.SetDescription("Coordinate hotseat games across players via a shared folder.");
        branch.AddCommand<GameInitCommand>("init")
            .WithDescription("Create a new game in a shared folder.");
        branch.AddCommand<GameJoinCommand>("join")
            .WithDescription("Join an existing game in a shared folder.");
        branch.AddCommand<GameListCommand>("list")
            .WithDescription("List all games configured on this machine.");
        branch.AddCommand<GameSwitchCommand>("switch")
            .WithDescription("Set which configured game is active.");
        branch.AddCommand<GameLeaveCommand>("leave")
            .WithDescription("Forget a game on this machine (shared folder untouched).");
        branch.AddCommand<GameDeleteCommand>("delete")
            .WithDescription("Permanently destroy a game in cloud storage AND remove the local entry.");
        branch.AddCommand<GameDiscoverCommand>("discover")
            .WithDescription("Scan a folder for shared games (turn_state.json).");
        branch.AddCommand<GameInviteCommand>("invite")
            .WithDescription("Print a one-line join command for the active game (paste in Discord).");
        branch.AddCommand<GamePackCommand>("pack")
            .WithDescription("Bundle the binary + stripped config.json into a zip you can send to friends.");
        branch.AddCommand<GameStatusCommand>("status")
            .WithDescription("Show whose turn it is in the active game.");
        branch.AddCommand<GameHistoryCommand>("history")
            .WithDescription("Print the full turn history of the active game.");
        branch.AddCommand<GameCheckCommand>("check")
            .WithDescription("If it's your turn, download the latest save into your Civ saves folder.");
        branch.AddCommand<GameSubmitCommand>("submit")
            .WithDescription("Upload the save you just played, advancing the turn.");
        branch.AddCommand<GameWatchCommand>("watch")
            .WithDescription("Run in the background, notifying you when it's your turn or you've saved.");
        branch.AddCommand<GameWebhookCommand>("webhook")
            .WithDescription("Set / clear / show the Discord webhook URL for this game's submit pings.");
        branch.AddCommand<GameRepairCommand>("repair")
            .WithDescription("Validate the active game's manifest and shared-folder contents.");
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

    // Brand-new users: walk them through identity + a first game.
    await MaybeRunWizardAsync(app);

    var topMenu = new[]
    {
        new MenuChoice("Sync",            new[] { "game", "watch" }),
        new MenuChoice("More options…",   MenuMarkers.Submenu),
        new MenuChoice("Exit",            null),
    };

    var subMenu = new[]
    {
        new MenuChoice("Whose turn? (one-shot)",        new[] { "game", "status"   }),
        new MenuChoice("Submit my turn (manual)",       new[] { "game", "submit"   }),
        new MenuChoice("Force re-download latest save", new[] { "game", "check"    }),
        new MenuChoice("List configured games",         new[] { "game", "list"     }),
        new MenuChoice("Switch active game",            new[] { "game", "switch"   }),
        new MenuChoice("Join another game",             MenuMarkers.JoinAnother    ),
        new MenuChoice("Game history",                  new[] { "game", "history"  }),
        new MenuChoice("Invite (paste link)",           new[] { "game", "invite"   }),
        new MenuChoice("Pack invite zip for friends",   new[] { "game", "pack"     }),
        new MenuChoice("Discord webhook (set/clear)",   new[] { "game", "webhook"  }),
        new MenuChoice("Storage defaults",              new[] { "defaults"         }),
        new MenuChoice("Repair / validate state",       new[] { "game", "repair"   }),
        new MenuChoice("Leave a game (this machine)",   new[] { "game", "leave"    }),
        new MenuChoice("Delete a game (from cloud)",    new[] { "game", "delete"   }),
        new MenuChoice("Install / update mod",          new[] { "install"          }),
        new MenuChoice("Uninstall mod",                 new[] { "uninstall"        }),
        new MenuChoice("Mod status",                    new[] { "status"           }),
        new MenuChoice("Reset (wipe local state)",      new[] { "reset"            }),
        new MenuChoice("Back",                          MenuMarkers.Back),
    };

    while (true)
    {
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<MenuChoice>()
                .Title("What do you want to do?")
                .UseConverter(c => c.Label)
                .AddChoices(topMenu));

        if (choice.Args is null) return 0;

        if (ReferenceEquals(choice.Args, MenuMarkers.Submenu))
        {
            await RunSubmenuAsync(app, subMenu);
            // The submenu's Reset action wipes config.json. Re-run the
            // wizard inline so the user lands on the start screen instead
            // of a top menu where every option would error.
            await ReRunWizardIfStateWipedAsync(app);
            continue;
        }

        await RunChoiceAsync(app, choice.Args);
        await ReRunWizardIfStateWipedAsync(app);
    }
}

static async Task RunSubmenuAsync(CommandApp app, MenuChoice[] choices)
{
    while (true)
    {
        AnsiConsole.Clear();
        DrawBanner();
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<MenuChoice>()
                .Title("More options")
                .UseConverter(c => c.Label)
                .PageSize(15)
                .AddChoices(choices));

        if (ReferenceEquals(choice.Args, MenuMarkers.Back)) return;
        if (choice.Args is null) Environment.Exit(0);

        var beforeGameCount = LocalConfig.Load().Games.Count;

        var args = ResolveMenuArgs(choice.Args);
        if (args is not null) await RunChoiceAsync(app, args);

        // If the action wiped local state (Reset), bail out so the top-loop's
        // wizard recheck can take over instead of looping in a submenu where
        // every remaining option would fail.
        if (!File.Exists(LocalConfig.ConfigPath)) return;

        // If the action just emptied the game list (leave/delete the last
        // game), drop straight into the create/join wizard — skipping the
        // name prompt — instead of leaving the user stranded at a submenu
        // where every remaining option would error.
        var after = LocalConfig.Load();
        if (beforeGameCount > 0
            && after.Games.Count == 0
            && !string.IsNullOrEmpty(after.PlayerName))
        {
            var wizardArgs = FirstRunWizard.RunCreateOrJoin(after.PlayerName);
            if (wizardArgs is not null) await DispatchWizardAsync(app, wizardArgs);
        }
    }
}

// Some menu choices are markers that need to build their args interactively
// before dispatch. Returns null if the user backed out — caller should skip
// dispatch and loop back to the menu.
static string[]? ResolveMenuArgs(string[] markerOrArgs)
{
    if (ReferenceEquals(markerOrArgs, MenuMarkers.JoinAnother))
    {
        var playerName = LocalConfig.Load().PlayerName;
        if (string.IsNullOrEmpty(playerName))
        {
            AnsiConsole.MarkupLine("[red]No player name set yet. Run the wizard first.[/]");
            return null;
        }
        return FirstRunWizard.RunInteractiveJoin(playerName);
    }
    return markerOrArgs;
}

static async Task RunChoiceAsync(CommandApp app, string[] args)
{
    AnsiConsole.WriteLine();
    await app.RunAsync(args);
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[grey]Press any key to return to the menu...[/]");
    Console.ReadKey(intercept: true);
    AnsiConsole.Clear();
    DrawBanner();
}

// Initial wizard kickoff at startup — runs whenever RunIfNeeded says so,
// including the partial-setup fast-path used by invite-zip configs.
static async Task MaybeRunWizardAsync(CommandApp app)
{
    var wizardArgs = FirstRunWizard.RunIfNeeded();
    if (wizardArgs is null) return;
    await DispatchWizardAsync(app, wizardArgs);
}

// Post-action recovery: only fire when config.json is gone (the reset
// signature). Skipping the wizard at startup leaves PlayerName set with
// no games — that state should NOT re-trigger the wizard or every menu
// action would force the user back through it.
static async Task ReRunWizardIfStateWipedAsync(CommandApp app)
{
    if (File.Exists(LocalConfig.ConfigPath)) return;
    var wizardArgs = FirstRunWizard.RunIfNeeded();
    if (wizardArgs is null) return;
    await DispatchWizardAsync(app, wizardArgs);
}

static async Task DispatchWizardAsync(CommandApp app, string[] wizardArgs)
{
    AnsiConsole.WriteLine();
    await app.RunAsync(wizardArgs);
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[grey]Press any key to continue to the menu...[/]");
    Console.ReadKey(intercept: true);
    AnsiConsole.Clear();
    DrawBanner();
}

static void DrawBanner()
{
    AnsiConsole.Write(new FigletText("civ6-async").Color(Color.Aqua));
    AnsiConsole.MarkupLine("[grey]Civilization VI hotseat helper[/]");
    AnsiConsole.WriteLine();
}

internal sealed record MenuChoice(string Label, string[]? Args);

internal static class MenuMarkers
{
    public static readonly string[] Submenu     = new[] { "__submenu__"     };
    public static readonly string[] Back        = new[] { "__back__"        };
    public static readonly string[] JoinAnother = new[] { "__join_another__"};
}
