using Spectre.Console;

namespace Civ6Async.Cli.Services;

/// <summary>
/// First-run setup for users who launch the helper before having any games
/// configured. Walks them through the bare minimum: their player name +
/// either creating or joining a game. Skipped silently for returning users.
/// </summary>
internal static class FirstRunWizard
{
    /// <summary>
    /// Runs the wizard if no config exists yet. Returns the args to invoke
    /// next (e.g. "game init …" or "game join …") or null if the user
    /// declined / cancelled.
    /// </summary>
    public static string[]? RunIfNeeded()
    {
        var config = LocalConfig.Load();
        if (config.PlayerName is not null && config.Games.Count > 0) return null;

        AnsiConsole.MarkupLine("[bold]Welcome to civ6-async[/]");
        AnsiConsole.MarkupLine("[grey]Let's get you set up. Two questions, then you're done.[/]");
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
            // Save just the player name so they don't get prompted again.
            config.PlayerName = name;
            config.Save();
            AnsiConsole.MarkupLine($"[grey]Saved name '{name.EscapeMarkup()}'. Run[/] [bold]civ6-async game init[/] [grey]or[/] [bold]game join[/] [grey]when ready.[/]");
            return null;
        }

        if (action.StartsWith("Create", StringComparison.OrdinalIgnoreCase))
        {
            var gameName = AnsiConsole.Prompt(new TextPrompt<string>("Game name (folder name + manifest title):"));
            var shared   = AnsiConsole.Prompt(new TextPrompt<string>("Shared folder root (your Dropbox/Drive folder):"));
            var players  = AnsiConsole.Prompt(new TextPrompt<string>("Players, comma-separated (turn order, including you):"));

            return new[] { "game", "init", gameName, "--shared", shared, "--players", players, "--me", name };
        }

        // Join.
        var sharedJoin = AnsiConsole.Prompt(new TextPrompt<string>("Shared folder for the game (must contain turn_state.json):"));
        return new[] { "game", "join", "--shared", sharedJoin, "--me", name };
    }
}
