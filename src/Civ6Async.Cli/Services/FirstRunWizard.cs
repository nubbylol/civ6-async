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
            storage = StorageFactory.From(config.ActiveGameEntry!);
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
        AnsiConsole.MarkupLine("[bold]Welcome to civ6-async[/]");
        AnsiConsole.MarkupLine("[grey]Let's get you set up. A few questions, then you're done.[/]");
        AnsiConsole.WriteLine();

        var name = AnsiConsole.Prompt(
            new TextPrompt<string>("Your player name (e.g. arin, max):")
                .Validate(s => string.IsNullOrWhiteSpace(s)
                    ? ValidationResult.Error("[red]Pick a name.[/]")
                    : ValidationResult.Success()));

        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Are you starting a new game, or joining one someone else created?")
                .AddChoices("Create a new game", "Join an existing game", "Skip — I'll do this later"));

        if (action.StartsWith("Skip", StringComparison.OrdinalIgnoreCase))
        {
            config.PlayerName = name;
            config.Save();
            AnsiConsole.MarkupLine($"[grey]Saved name '{name.EscapeMarkup()}'. Run[/] [bold]civ6-async game init[/] [grey]or[/] [bold]game join[/] [grey]when ready.[/]");
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
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Generate a Dropbox access token at[/] [bold]https://www.dropbox.com/developers/apps[/][grey] →[/]");
            AnsiConsole.MarkupLine("[grey]  1. Create app → Scoped, App folder.[/]");
            AnsiConsole.MarkupLine("[grey]  2. Permissions tab: tick files.content.read, files.content.write, files.metadata.read.[/]");
            AnsiConsole.MarkupLine("[grey]  3. Settings tab: Generated access token → Generate.[/]");
            var token = AnsiConsole.Prompt(new TextPrompt<string>("Dropbox access token:")
                .Secret('*'));
            return new[] { "game", "init", gameName, "--provider", "dropbox",
                "--dropbox-token", token, "--players", players, "--me", name };
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Local cloud-synced folder root (each game gets a subfolder).[/]");
        AnsiConsole.MarkupLine("[grey]Example: G:\\My Drive\\civ6-async  or  ~/Dropbox/civ6-async[/]");
        var shared = AnsiConsole.Prompt(new TextPrompt<string>("Shared folder root:"));
        return new[] { "game", "init", gameName, "--shared", shared, "--players", players, "--me", name };
    }

    private static string[]? BuildJoinArgs(string name, bool dropbox)
    {
        if (dropbox) return BuildDropboxJoinArgs(name);
        return BuildLocalJoinArgs(name);
    }

    private static string[]? BuildDropboxJoinArgs(string name)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Paste the access token the host sent you.[/]");
        var token = AnsiConsole.Prompt(new TextPrompt<string>("Dropbox access token:").Secret('*'));

        AnsiConsole.MarkupLine("[grey]Folder root to scan. Press Enter for the App folder root (default).[/]");
        var rootPath = AnsiConsole.Prompt(
            new TextPrompt<string>("Dropbox root folder:")
                .AllowEmpty()
                .DefaultValue(""));

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
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Cloud-synced folder root the host put games under.[/]");
        AnsiConsole.MarkupLine("[grey]Example: G:\\My Drive\\civ6-async  or  ~/Dropbox/civ6-async[/]");
        var rootPath = AnsiConsole.Prompt(new TextPrompt<string>("Folder root:"));

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
}
