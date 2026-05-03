using System.ComponentModel;
using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

/// <summary>
/// Manual fallback for the auto-submit path. Most users won't run this —
/// 'game watch' submits in the background as soon as Civ writes a save.
/// Useful for headless / CLI / non-watch scenarios.
/// </summary>
internal sealed class GameSubmitCommand : Command<GameSubmitCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--save <PATH>")]
        [Description("Save file to submit. If omitted, auto-picks the most recent .Civ6Save in the Civ saves folder.")]
        public string? SavePath { get; init; }

        [CommandOption("--pick")]
        [Description("Force the interactive picker over recent saves instead of auto-picking the latest.")]
        public bool Pick { get; init; }

        [CommandOption("-f|--force")]
        [Description("Submit even if conflicts are detected. Use only when you know what you're doing.")]
        public bool Force { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (config, manifest, err) = GameContext.Resolve();
        if (err is not null) { AnsiConsole.MarkupLine(err); return 1; }

        var savePath = ResolveSavePath(settings);
        if (savePath is null) return 1;

        var result = SubmitFlow.Run(config!, manifest!, savePath, settings.Force);
        return result.Outcome == SubmitFlow.Outcome.Submitted ? 0 : 1;
    }

    private static string? ResolveSavePath(Settings settings)
    {
        if (settings.SavePath is not null) return settings.SavePath;
        if (settings.Pick) return SavePicker.Pick(null);

        // Auto-pick: most recent .Civ6Save not authored by us (skip
        // civ6-async-* downloads, since those are what we'd just put there).
        var savesDir = PlatformPaths.AutoDetectHotseatSavesDir();
        if (savesDir is null || !Directory.Exists(savesDir))
        {
            AnsiConsole.MarkupLine("[red]Civ 6 hotseat saves folder not found.[/]");
            return null;
        }

        var picked = new DirectoryInfo(savesDir)
            .EnumerateFiles("*.Civ6Save")
            .Where(f => !f.Name.StartsWith(SavePicker.DownloadedSavePrefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();

        if (picked is null)
        {
            AnsiConsole.MarkupLine(
                "[yellow]No recent saves found in[/] [grey]" + savesDir.EscapeMarkup() + "[/]. " +
                "Make sure you've saved the game in Civ first, or pass [bold]--pick[/] to choose a different file.");
            return null;
        }

        AnsiConsole.MarkupLine(
            $"[grey]Auto-picked latest save:[/] [bold]{picked.Name.EscapeMarkup()}[/] " +
            $"[grey]({FormatRelative(picked.LastWriteTime)})[/]");
        return picked.FullName;
    }

    private static string FormatRelative(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalSeconds < 60) return "just now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m ago";
        if (d.TotalHours   < 24) return $"{(int)d.TotalHours}h ago";
        return $"{(int)d.TotalDays}d ago";
    }
}
