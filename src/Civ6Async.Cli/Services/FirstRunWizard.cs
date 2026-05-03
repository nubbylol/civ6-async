using Civ6Async.Cli.Services.Storage;
using Spectre.Console;

namespace Civ6Async.Cli.Services;

internal static class FirstRunWizard
{
    public static string[]? RunIfNeeded()
    {
        var config = LocalConfig.Load();

        // Already fully set up — no wizard.
        if (config.PlayerName is not null && config.Games.Count > 0) return null;

        // Partial setup: a game is already registered (likely from a
        // pre-shipped config.json) but the player name is missing. Skip
        // the create/join flow entirely; just identify the player.
        if (config.PlayerName is null && config.Games.Count > 0 && config.ActiveGame is not null)
        {
            return CompletePartialSetup(config);
        }

        // Empty config: full onboarding.
        return RunFullWizard(config);
    }

    /// <summary>
    /// Fast-path for friends running a pre-populated config.json. Reads the
    /// active game's manifest, shows the player list, asks which one they
    /// are. Saves and returns null (no follow-up command needed).
    /// </summary>
    private static string[]? CompletePartialSetup(LocalConfig config)
    {
        AnsiConsole.MarkupLine("[bold]Welcome to civ6-async[/]");
        AnsiConsole.MarkupLine($"[grey]A game is already configured: [/][bold]{config.ActiveGame!.EscapeMarkup()}[/]");
        AnsiConsole.WriteLine();

        IGameStorage storage;
        try
        {
            storage = StorageFactory.From(config, config.ActiveGameEntry!);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Couldn't open the configured storage:[/] {ex.Message.EscapeMarkup()}");
            AnsiConsole.MarkupLine("Edit [bold]config.json[/] or run [bold]game init[/] / [bold]game join[/] manually.");
            return null;
        }

        try
        {
            GameManifest? manifest = null;
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start($"Reading {storage.Description}…", _ => manifest = GameManifest.TryLoad(storage));

            if (manifest is null)
            {
                AnsiConsole.MarkupLine("[red]Couldn't read the game manifest from the configured storage.[/]");
                AnsiConsole.MarkupLine(
                    "Likely causes: bad token, wrong folder path, or the host hasn't created " +
                    "the game yet. Edit [bold]config.json[/] or use [bold]game join[/] with the right values.");
                return null;
            }

            var name = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Which player are you?")
                    .AddChoices(manifest.Players));

            config.PlayerName = name;
            config.Save();

            AnsiConsole.MarkupLine(
                $"[green]Set up as[/] [bold]{name.EscapeMarkup()}[/] [green]in[/] [bold]{manifest.GameName.EscapeMarkup()}[/][green].[/]");
            AnsiConsole.MarkupLine(
                $"Currently on turn [bold]{manifest.CurrentTurn}[/], waiting on [yellow]{manifest.CurrentPlayer.EscapeMarkup()}[/].");
            AnsiConsole.WriteLine();

            ModBootstrap.EnsureInstalled();
            return null;
        }
        finally
        {
            (storage as IDisposable)?.Dispose();
        }
    }

    private static string[]? RunFullWizard(LocalConfig config)
    {
        var hasName = !string.IsNullOrWhiteSpace(config.PlayerName);

        AnsiConsole.MarkupLine(hasName ? "[bold]civ6-async[/]" : "[bold]Welcome to civ6-async[/]");
        AnsiConsole.MarkupLine(hasName
            ? $"[grey]You're set up as[/] [bold]{config.PlayerName!.EscapeMarkup()}[/][grey], but you're not in a game right now.[/]"
            : "[grey]Let's get you set up. A few questions, then you're done.[/]");
        AnsiConsole.WriteLine();

        var name = hasName
            ? config.PlayerName!
            : AnsiConsole.Prompt(
                new TextPrompt<string>("Your player name (e.g. arin, max):")
                    .Validate(s => string.IsNullOrWhiteSpace(s)
                        ? ValidationResult.Error("[red]Pick a name.[/]")
                        : ValidationResult.Success()));

        return RunCreateOrJoinFlow(name, persistNameOnSkip: !hasName);
    }

    /// <summary>
    /// Re-entry point for the menu when the user has just left or deleted
    /// their last game. They already have a saved player name, so skip
    /// straight to the create/join branch — no name prompt, no Skip option.
    /// Returns args for game init / game join, or null if cancelled.
    /// </summary>
    public static string[]? RunCreateOrJoin(string playerName)
    {
        AnsiConsole.MarkupLine("[bold]Set up a game[/]");
        AnsiConsole.MarkupLine($"[grey]Hi[/] [bold]{playerName.EscapeMarkup()}[/][grey] — you're not in a game right now. Let's fix that.[/]");
        AnsiConsole.WriteLine();
        return RunCreateOrJoinFlow(playerName, persistNameOnSkip: false);
    }

    /// <summary>
    /// Shared body of the wizard: pick Create vs. Join (vs. Skip on first
    /// run, vs. Cancel afterwards), pick provider, dispatch to the right
    /// args-builder. <paramref name="persistNameOnSkip"/> is true only on
    /// the very first run, when picking Skip means "save the name and
    /// drop me at the menu so I can do it later".
    /// </summary>
    private static string[]? RunCreateOrJoinFlow(string name, bool persistNameOnSkip)
    {
        var bailLabel = persistNameOnSkip ? "Skip — I'll do this later" : "Cancel";
        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Create a new game, or join one someone else created?")
                .AddChoices("Create a new game", "Join an existing game", bailLabel));

        if (action == bailLabel)
        {
            if (persistNameOnSkip)
            {
                var c = LocalConfig.Load();
                c.PlayerName = name;
                c.Save();
                AnsiConsole.MarkupLine($"[grey]Saved name '{name.EscapeMarkup()}'. Run[/] [bold]civ6-async game init[/] [grey]or[/] [bold]game join[/] [grey]when ready.[/]");
            }
            return null;
        }

        var provider = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Storage provider:")
                .AddChoices(
                    "Dropbox (direct API — recommended; needs an access token from the host)",
                    "Local folder (Drive/Dropbox/OneDrive desktop sync — slower)"));

        var isDropbox = provider.StartsWith("Dropbox", StringComparison.OrdinalIgnoreCase);

        if (action.StartsWith("Create", StringComparison.OrdinalIgnoreCase))
            return BuildInitArgs(name, isDropbox);

        return BuildJoinArgs(name, isDropbox);
    }

    private static string[] BuildInitArgs(string name, bool dropbox)
    {
        var savedDefaults = LocalConfig.Load();

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]A short label for this game (becomes a folder name).[/]");
        AnsiConsole.MarkupLine("[grey]Example: PangaeaDuel[/]");
        var gameName = AnsiConsole.Prompt(new TextPrompt<string>("Game name:"));

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Comma-separated turn order — include yourself.[/]");
        AnsiConsole.MarkupLine("[grey]Example: arin,max,jess[/]");
        var players = AnsiConsole.Prompt(new TextPrompt<string>("Players:"));

        if (dropbox)
        {
            var token = PromptOrReuseDropboxToken(savedDefaults.DropboxToken);

            var rootPath = PromptDropboxRoot(savedDefaults.DefaultDropboxRoot);
            PersistDefaultDropboxRoot(rootPath);

            var args = new List<string> {
                "game", "init", gameName, "--provider", "dropbox",
                "--dropbox-token", token, "--players", players, "--me", name,
            };
            if (!string.IsNullOrEmpty(rootPath))
            {
                args.Add("--dropbox-folder");
                args.Add(rootPath);
            }
            return args.ToArray();
        }

        var shared = PromptLocalRoot(savedDefaults.DefaultSharedRoot);
        PersistDefaultSharedRoot(shared);
        return new[] { "game", "init", gameName, "--shared", shared, "--players", players, "--me", name };
    }

    /// <summary>
    /// Re-entry point for the menu's "Join another game" action. Reuses the
    /// wizard's discover/pick/name-resolve flow, skipping the first-run
    /// name + create/join/skip prompts. Returns the args to dispatch
    /// against <c>game join</c>, or null if the user backed out.
    /// </summary>
    public static string[]? RunInteractiveJoin(string playerName)
    {
        AnsiConsole.MarkupLine("[bold]Join another game[/]");
        AnsiConsole.WriteLine();

        var provider = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Storage provider:")
                .AddChoices(
                    "Dropbox (direct API — recommended; needs an access token from the host)",
                    "Local folder (Drive/Dropbox/OneDrive desktop sync — slower)",
                    "Cancel"));

        if (provider.StartsWith("Cancel", StringComparison.OrdinalIgnoreCase))
            return null;

        var isDropbox = provider.StartsWith("Dropbox", StringComparison.OrdinalIgnoreCase);
        return BuildJoinArgs(playerName, isDropbox);
    }

    private static string[]? BuildJoinArgs(string name, bool dropbox)
    {
        if (dropbox) return BuildDropboxJoinArgs(name);
        return BuildLocalJoinArgs(name);
    }

    private static string[]? BuildDropboxJoinArgs(string name)
    {
        var savedConfig = LocalConfig.Load();
        var token = PromptOrReuseDropboxToken(savedConfig.DropboxToken);

        var rootPath = PromptDropboxRoot(savedConfig.DefaultDropboxRoot);
        PersistDefaultDropboxRoot(rootPath);

        var rootLabel = string.IsNullOrEmpty(rootPath) ? "App folder root" : rootPath;
        IReadOnlyList<GameDiscovery.Found>? games = null;
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start($"Looking for games under {rootLabel}…",
                _ => games = GameDiscovery.Dropbox(token, rootPath));

        if (games is null || games.Count == 0)
        {
            AnsiConsole.MarkupLine(
                "[yellow]No games found at that root.[/] Either the token's wrong, the folder path doesn't match what the host used, or the host hasn't created the game yet.");
            // Fall back to manual entry.
            AnsiConsole.MarkupLine("[grey]You can paste the full game-folder path the host sent you instead.[/]");
            var folder = AnsiConsole.Prompt(new TextPrompt<string>("Dropbox folder (full path):"));
            return new[] { "game", "join", "--dropbox-token", token, "--dropbox-folder", folder, "--me", name };
        }

        var picked = games.Count == 1
            ? games[0]
            : AnsiConsole.Prompt(
                new SelectionPrompt<GameDiscovery.Found>()
                    .Title("Pick a game to join:")
                    .UseConverter(g => $"{g.Name}  [grey](T{g.Manifest.CurrentTurn}, waiting on {g.Manifest.CurrentPlayer})[/]")
                    .AddChoices(games));

        if (games.Count == 1)
            AnsiConsole.MarkupLine($"[grey]Found one game:[/] [bold]{picked.Name.EscapeMarkup()}[/]");

        var resolvedName = ResolveNameInManifest(name, picked.Manifest);
        return new[] { "game", "join", "--dropbox-token", token, "--dropbox-folder", picked.FullPath, "--me", resolvedName };
    }

    private static string[]? BuildLocalJoinArgs(string name)
    {
        var rootPath = PromptLocalRoot(LocalConfig.Load().DefaultSharedRoot);
        PersistDefaultSharedRoot(rootPath);

        var games = GameDiscovery.Local(rootPath);

        if (games.Count == 0)
        {
            AnsiConsole.MarkupLine(
                "[yellow]No games found at that root.[/] Either the path is wrong, the host's sync hasn't propagated yet, or the host hasn't created the game.");
            AnsiConsole.MarkupLine("[grey]You can paste the full game-folder path instead (the one containing turn_state.json).[/]");
            var folder = AnsiConsole.Prompt(new TextPrompt<string>("Game folder (full path):"));
            return new[] { "game", "join", "--shared", folder, "--me", name };
        }

        var picked = games.Count == 1
            ? games[0]
            : AnsiConsole.Prompt(
                new SelectionPrompt<GameDiscovery.Found>()
                    .Title("Pick a game to join:")
                    .UseConverter(g => $"{g.Name}  [grey](T{g.Manifest.CurrentTurn}, waiting on {g.Manifest.CurrentPlayer})[/]")
                    .AddChoices(games));

        if (games.Count == 1)
            AnsiConsole.MarkupLine($"[grey]Found one game:[/] [bold]{picked.Name.EscapeMarkup()}[/]");

        var resolvedName = ResolveNameInManifest(name, picked.Manifest);
        return new[] { "game", "join", "--shared", picked.FullPath, "--me", resolvedName };
    }

    /// <summary>
    /// If the typed name matches a player in the manifest (case-insensitive)
    /// it's used as-is. Otherwise, list the manifest's players so the user
    /// picks which one they actually are. This catches cases where the host
    /// registered the player under a slightly different spelling.
    /// </summary>
    private static string ResolveNameInManifest(string typedName, GameManifest manifest)
    {
        if (manifest.Players.Any(p => p.Equals(typedName, StringComparison.OrdinalIgnoreCase)))
            return typedName;

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[yellow]'{typedName.EscapeMarkup()}' isn't a player in this game.[/]");
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Which player are you?")
                .AddChoices(manifest.Players));
    }

    /// <summary>
    /// If a per-machine Dropbox token is already saved, reuse it silently.
    /// Otherwise prompt the user for one (with the create-an-app
    /// instructions, which only matter for first-time setup). Tokens are
    /// per-machine — once you've supplied one, you never have to type it
    /// again for additional games.
    /// </summary>
    private static string PromptOrReuseDropboxToken(string? savedToken)
    {
        if (!string.IsNullOrEmpty(savedToken))
        {
            AnsiConsole.MarkupLine("[grey]Using your saved Dropbox access token.[/]");
            return savedToken;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Generate a Dropbox access token at[/] [bold]https://www.dropbox.com/developers/apps[/][grey] →[/]");
        AnsiConsole.MarkupLine("[grey]  1. Create app → Scoped, App folder.[/]");
        AnsiConsole.MarkupLine("[grey]  2. Permissions tab: tick files.content.read, files.content.write, files.metadata.read.[/]");
        AnsiConsole.MarkupLine("[grey]  3. Settings tab: Generated access token → Generate.[/]");
        return AnsiConsole.Prompt(new TextPrompt<string>("Dropbox access token:").Secret('*'));
    }

    // ---- shared root prompts (used by init + join, default to user's saved value) ----

    private static string PromptDropboxRoot(string? savedDefault)
    {
        var defaultValue = savedDefault ?? "";
        var hint = string.IsNullOrEmpty(defaultValue)
            ? "[grey]Folder root inside your Dropbox app folder. Press Enter for the App folder root.[/]"
            : $"[grey]Folder root inside your Dropbox app folder. Press Enter to use [bold]{defaultValue.EscapeMarkup()}[/][grey] (your saved default).[/]";
        AnsiConsole.MarkupLine(hint);
        return AnsiConsole.Prompt(
            new TextPrompt<string>("Dropbox root folder:")
                .AllowEmpty()
                .DefaultValue(defaultValue));
    }

    private static string PromptLocalRoot(string? savedDefault)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Cloud-synced folder root (each game is a subfolder of this).[/]");
        AnsiConsole.MarkupLine("[grey]Example: G:\\My Drive\\civ6-async  or  ~/Dropbox/civ6-async[/]");
        var prompt = new TextPrompt<string>("Folder root:");
        if (!string.IsNullOrEmpty(savedDefault)) prompt.DefaultValue(savedDefault);
        return AnsiConsole.Prompt(prompt);
    }

    private static void PersistDefaultDropboxRoot(string root)
    {
        var c = LocalConfig.Load();
        if (string.Equals(c.DefaultDropboxRoot ?? "", root ?? "", StringComparison.Ordinal)) return;
        c.DefaultDropboxRoot = root;
        c.Save();
    }

    private static void PersistDefaultSharedRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return;
        var c = LocalConfig.Load();
        if (string.Equals(c.DefaultSharedRoot, root, StringComparison.Ordinal)) return;
        c.DefaultSharedRoot = root;
        c.Save();
    }
}
