using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

/// <summary>
/// Status now does the smart thing: shows whose turn it is, and if it's
/// yours and you don't have the latest save downloaded yet, prompts to
/// download right there. The user shouldn't have to know about a separate
/// "check" step.
/// </summary>
internal sealed class GameStatusCommand : Command<EmptySettings>
{
    protected override int Execute(CommandContext context, EmptySettings settings, CancellationToken cancellationToken)
    {
        var (config, manifest, err) = GameContext.Resolve();
        if (err is not null) { AnsiConsole.MarkupLine(err); return 1; }

        var iAmUp = string.Equals(manifest!.CurrentPlayer, config!.PlayerName,
            StringComparison.OrdinalIgnoreCase);

        var grid = new Grid().AddColumn().AddColumn();
        grid.AddRow("Game",        $"[bold]{manifest.GameName.EscapeMarkup()}[/]");
        grid.AddRow("Shared",      $"[grey]{config.ActiveGameEntry!.SharedFolderPath.EscapeMarkup()}[/]");
        grid.AddRow("Turn",        manifest.CurrentTurn.ToString());
        grid.AddRow("Whose turn",  iAmUp
            ? $"[green]{manifest.CurrentPlayer}[/] (you)"
            : $"[yellow]{manifest.CurrentPlayer}[/]");
        grid.AddRow("Players",     string.Join(" → ", manifest.Players));
        if (manifest.LatestSaveSubmittedAt is not null)
            grid.AddRow("Last submit", $"[grey]{manifest.LatestSaveSubmittedAt.Value:yyyy-MM-dd HH:mm} UTC ({FormatRelative(manifest.LatestSaveSubmittedAt.Value)})[/]");

        AnsiConsole.Write(grid);
        AnsiConsole.WriteLine();

        if (!iAmUp)
        {
            AnsiConsole.MarkupLine($"Waiting on [yellow]{manifest.CurrentPlayer}[/].");
            return 0;
        }

        // It's our turn. Figure out whether we have the right save locally
        // and offer to download if not.
        var plan = SaveDownloader.Inspect(config, manifest);
        switch (plan.Status)
        {
            case SaveDownloader.Status.NoSaveYet:
                AnsiConsole.MarkupLine(
                    "It's [green]your turn[/] — turn 1. There's no shared save yet; play your first turn " +
                    "in Civ from a fresh hotseat game, then submit.");
                return 0;

            case SaveDownloader.Status.SavesDirMissing:
                AnsiConsole.MarkupLine(
                    "It's [green]your turn[/], but Civ 6's saves folder wasn't found. " +
                    "Has Civ been launched on this machine yet?");
                return 1;

            case SaveDownloader.Status.SourceMissing:
                AnsiConsole.MarkupLine(
                    $"It's [green]your turn[/], but the latest save " +
                    $"[grey]{manifest.LatestSaveFile!.EscapeMarkup()}[/] hasn't synced from the cloud yet. " +
                    "Wait for sync and run again.");
                return 1;

            case SaveDownloader.Status.AlreadyHave:
                AnsiConsole.MarkupLine(
                    $"It's [green]your turn[/]. Latest save already downloaded as " +
                    $"[bold]{plan.DestName!.EscapeMarkup()}[/]. Open it in Civ to play.");
                return 0;

            case SaveDownloader.Status.Stale:
                AnsiConsole.MarkupLine(
                    $"It's [green]your turn[/] (turn {manifest.CurrentTurn}). The latest save isn't on this machine yet.");
                if (AnsiConsole.Confirm("Download it now?"))
                {
                    SaveDownloader.Execute(plan);
                    AnsiConsole.MarkupLine(
                        $"[green]Downloaded[/] → [grey]{plan.DestPath!.EscapeMarkup()}[/]");
                    AnsiConsole.MarkupLine(
                        $"Open [bold]{plan.DestName!.EscapeMarkup()}[/] in Civ, play, save, then submit.");
                }
                return 0;
        }

        return 0;
    }

    private static string FormatRelative(DateTime utc)
    {
        var delta = DateTime.UtcNow - utc;
        if (delta.TotalSeconds < 60) return "just now";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes}m ago";
        if (delta.TotalHours   < 24) return $"{(int)delta.TotalHours}h ago";
        return $"{(int)delta.TotalDays}d ago";
    }
}

internal sealed class EmptySettings : CommandSettings { }
