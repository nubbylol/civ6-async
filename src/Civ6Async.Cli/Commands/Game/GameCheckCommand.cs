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

        if (!iAmUp)
        {
            AnsiConsole.MarkupLine(
                $"It's [yellow]{manifest.CurrentPlayer}[/]'s turn (turn {manifest.CurrentTurn}). " +
                "Nothing for you to do yet.");
            return 0;
        }

        if (manifest.LatestSaveFile is null)
        {
            AnsiConsole.MarkupLine(
                $"It's [green]your turn[/] — turn 1. There's no shared save yet; play your first turn " +
                "in Civ from a fresh hotseat game, then run [bold]game submit[/].");
            return 0;
        }

        var savesDir = PlatformPaths.AutoDetectHotseatSavesDir();
        if (savesDir is null)
        {
            AnsiConsole.MarkupLine("[red]Civ 6 hotseat saves folder not found.[/] Has Civ been launched on this machine yet?");
            return 1;
        }
        Directory.CreateDirectory(savesDir);

        var src = Path.Combine(config.ActiveGameEntry!.SharedFolderPath, manifest.LatestSaveFile);
        if (!File.Exists(src))
        {
            AnsiConsole.MarkupLine(
                $"[red]Manifest references[/] [grey]{manifest.LatestSaveFile.EscapeMarkup()}[/] " +
                $"[red]but the file isn't in the shared folder yet.[/] " +
                "Wait for cloud sync to finish, then try again.");
            return 1;
        }

        var destName = SavePicker.DownloadedSaveName(manifest.GameName, manifest.CurrentTurn);
        var dest     = Path.Combine(savesDir, destName);
        File.Copy(src, dest, overwrite: true);

        AnsiConsole.MarkupLine($"[green]Your turn[/] (turn {manifest.CurrentTurn}).");
        AnsiConsole.MarkupLine(
            $"Downloaded [grey]{manifest.LatestSaveFile.EscapeMarkup()}[/] " +
            $"→ [grey]{dest.EscapeMarkup()}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"Open [bold]{destName.EscapeMarkup()}[/] in Civilization VI, play your turn, save the " +
            "game, then run [bold]civ6-async game submit[/].");
        return 0;
    }
}
