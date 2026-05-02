using System.ComponentModel;
using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

internal sealed class GameSubmitCommand : Command<GameSubmitCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--save <PATH>")]
        [Description("Save file to submit. If omitted, an interactive picker over your Civ saves runs.")]
        public string? SavePath { get; init; }

        [CommandOption("-f|--force")]
        [Description("Submit even if conflicts are detected. Use only when you know what you're doing.")]
        public bool Force { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (config, manifest, err) = GameContext.Resolve();
        if (err is not null) { AnsiConsole.MarkupLine(err); return 1; }

        var savePath = settings.SavePath ?? SavePicker.Pick(null);
        if (savePath is null) return 1;
        if (!File.Exists(savePath))
        {
            AnsiConsole.MarkupLine($"[red]Save file not found:[/] [grey]{savePath.EscapeMarkup()}[/]");
            return 1;
        }

        var conflicts = ConflictDetector.Detect(manifest!, config!.PlayerName!, savePath);
        if (conflicts.Count > 0)
        {
            foreach (var c in conflicts) PrintConflict(c);
            if (!settings.Force)
            {
                AnsiConsole.MarkupLine(
                    "\n[red]Submit refused.[/] Re-run with [bold]--force[/] to override.");
                return 1;
            }
            AnsiConsole.MarkupLine("\n[yellow]--force given; submitting anyway.[/]");
        }

        // Name the file in the shared folder so a glance at the folder shows
        // who submitted what. Original local name is preserved on the player's
        // own machine.
        var dstName = $"{manifest!.GameName}_T{manifest.CurrentTurn:D3}_{config!.PlayerName}.Civ6Save";
        var dst     = Path.Combine(config.ActiveGame!.SharedFolderPath, dstName);
        File.Copy(savePath, dst, overwrite: true);

        var hash = GameManifest.HashFile(dst);
        var fromTurn   = manifest.CurrentTurn;
        var fromPlayer = config.PlayerName!;
        manifest.AdvanceTurn(fromPlayer, fromTurn, dstName, hash);
        manifest.Save(config.ActiveGame.SharedFolderPath);

        AnsiConsole.MarkupLine(
            $"[green]Submitted turn {fromTurn} as[/] [grey]{dstName.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"Next up: [yellow]{manifest.CurrentPlayer}[/] (turn {manifest.CurrentTurn}).");
        return 0;
    }

    private static void PrintConflict(SubmitConflict c)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[yellow]⚠  {c.Title}[/]");
        AnsiConsole.MarkupLine($"   {c.Detail}");
        AnsiConsole.MarkupLine($"   [bold]How to fix:[/] {c.Remediation}");
    }
}
