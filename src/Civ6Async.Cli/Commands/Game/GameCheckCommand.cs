using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

internal sealed class GameCheckCommand : Command<EmptySettings>
{
    protected override int Execute(CommandContext context, EmptySettings settings, CancellationToken cancellationToken)
    {
        var (config, manifest, err) = GameContext.Resolve();
        if (err is not null) { AnsiConsole.MarkupLine(err); return 1; }

        var iAmUp = string.Equals(manifest!.CurrentPlayer, config!.PlayerName,
            StringComparison.OrdinalIgnoreCase);

        // Note: we no longer refuse to download when it's not the user's turn.
        // Downloading is read-only — the conflict detector still prevents bad
        // submits — and people sometimes legitimately want a copy of the
        // current state to review.
        var plan = SaveDownloader.Inspect(config, manifest);
        switch (plan.Status)
        {
            case SaveDownloader.Status.NoSaveYet:
                AnsiConsole.MarkupLine(iAmUp
                    ? "It's [green]your turn[/] — turn 1. There's no shared save yet; play your first turn " +
                      "in Civ from a fresh hotseat game, then run [bold]game submit[/]."
                    : $"No shared save yet (game is on turn {manifest.CurrentTurn}, waiting on " +
                      $"[yellow]{manifest.CurrentPlayer}[/]).");
                return 0;

            case SaveDownloader.Status.SavesDirMissing:
                AnsiConsole.MarkupLine("[red]Civ 6 hotseat saves folder not found.[/] Has Civ been launched on this machine yet?");
                return 1;

            case SaveDownloader.Status.SourceMissing:
                AnsiConsole.MarkupLine(
                    $"[red]Manifest references[/] [grey]{manifest.LatestSaveFile!.EscapeMarkup()}[/] " +
                    "[red]but the file isn't in the shared folder yet.[/] " +
                    "Wait for cloud sync to finish, then try again.");
                return 1;

            case SaveDownloader.Status.AlreadyHave:
                AnsiConsole.MarkupLine(iAmUp
                    ? $"[green]Your turn[/] (turn {manifest.CurrentTurn})."
                    : $"Game is on turn {manifest.CurrentTurn}, waiting on [yellow]{manifest.CurrentPlayer}[/].");
                AnsiConsole.MarkupLine(
                    $"Latest save already downloaded → [grey]{plan.DestPath!.EscapeMarkup()}[/]");
                AnsiConsole.MarkupLine(iAmUp
                    ? $"Open [bold]{plan.DestName!.EscapeMarkup()}[/] in Civilization VI to play."
                    : $"You can open [bold]{plan.DestName!.EscapeMarkup()}[/] in Civ to review state, but don't " +
                      "submit — it's not your turn yet.");
                return 0;

            case SaveDownloader.Status.Stale:
                SaveDownloader.Execute(plan);
                AnsiConsole.MarkupLine(iAmUp
                    ? $"[green]Your turn[/] (turn {manifest.CurrentTurn})."
                    : $"Game is on turn {manifest.CurrentTurn}, waiting on [yellow]{manifest.CurrentPlayer}[/].");
                AnsiConsole.MarkupLine(
                    $"Downloaded [grey]{manifest.LatestSaveFile!.EscapeMarkup()}[/] " +
                    $"→ [grey]{plan.DestPath!.EscapeMarkup()}[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine(iAmUp
                    ? $"Open [bold]{plan.DestName!.EscapeMarkup()}[/] in Civilization VI, play your turn, save the " +
                      "game, then run [bold]civ6-async game submit[/]."
                    : $"You can open [bold]{plan.DestName!.EscapeMarkup()}[/] in Civ to review state, but don't " +
                      "submit — it's not your turn yet.");
                return 0;
        }

        return 0;
    }
}
