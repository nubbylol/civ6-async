using Spectre.Console;

namespace Civ6Async.Cli.Services;

internal static class FirstRunWizard
{
    public static string[]? RunIfNeeded()
    {
        var config = LocalConfig.Load();
        if (config.PlayerName is not null && config.Games.Count > 0) return null;

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

    private static string[] BuildJoinArgs(string name, bool dropbox)
    {
        if (dropbox)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Paste the access token the host sent you.[/]");
            var token = AnsiConsole.Prompt(new TextPrompt<string>("Dropbox access token:").Secret('*'));
            AnsiConsole.MarkupLine("[grey]Paste the Dropbox folder path the host sent (e.g. /civ6-async/PangaeaDuel).[/]");
            var folder = AnsiConsole.Prompt(new TextPrompt<string>("Dropbox folder:"));
            return new[] { "game", "join", "--dropbox-token", token, "--dropbox-folder", folder, "--me", name };
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Full path to the game's folder — the one containing turn_state.json.[/]");
        AnsiConsole.MarkupLine("[grey]Example: G:\\My Drive\\civ6-async\\PangaeaDuel[/]");
        AnsiConsole.MarkupLine("[grey]The host can give you the exact path with [bold]civ6-async game invite[/].[/]");
        var sharedJoin = AnsiConsole.Prompt(new TextPrompt<string>("Game folder:"));
        return new[] { "game", "join", "--shared", sharedJoin, "--me", name };
    }
}
