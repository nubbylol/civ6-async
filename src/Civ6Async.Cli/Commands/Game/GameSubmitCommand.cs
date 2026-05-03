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

        // Acquire the cooperative lock so a concurrent submit from another
        // helper instance can't race our manifest write.
        if (!SubmitLock.TryAcquire(config!.ActiveGameEntry!.SharedFolderPath, config.PlayerName!, out var blocking))
        {
            AnsiConsole.MarkupLine(
                $"[red]Another submit is already in progress.[/] " +
                $"Held by [yellow]{blocking!.Player}[/] on " +
                $"[yellow]{blocking.Hostname}[/] since " +
                $"[grey]{blocking.AcquiredAt:yyyy-MM-dd HH:mm:ss}[/] UTC.");
            AnsiConsole.MarkupLine(
                $"   Either wait for it to finish, or — if it looks dead (older than " +
                $"{SubmitLock.StaleAfter.TotalMinutes:F0} minutes) — rerun with [bold]--force[/].");
            return 1;
        }

        try
        {
            // Pull the actual game state from the most recent EventLogger
            // save_complete event — this is the source of truth even if the
            // in-game mod auto-cycled through several players' turns during
            // the user's session.
            var saveEvent = LuaLogReader.FindLatest(new[] { "save_complete" });

            int    realTurn;
            string nextPlayer;

            if (saveEvent is { Turn: int t, Player: { } p })
            {
                realTurn   = t;
                nextPlayer = ResolveNextPlayer(p, manifest!.Players);
                AnsiConsole.MarkupLine(
                    $"[grey]Lua.log says save was at turn {t}, active player {p}.[/]");
            }
            else
            {
                // No event — fall back to the manifest's current state and
                // the dumb "next player in list" advance. Warn loudly.
                realTurn   = manifest!.CurrentTurn;
                nextPlayer = manifest.Players[(manifest.Players.IndexOf(config.PlayerName!) + 1) % manifest.Players.Count];
                AnsiConsole.MarkupLine(
                    "[yellow]Warning:[/] no civ6-async save event found in Lua.log. " +
                    "Submitting with assumed turn/player advance — verify state after.");
            }

            // Name the file in the shared folder so a glance at the folder shows
            // who submitted what.
            var dstName = $"{manifest.GameName}_T{realTurn:D3}_{config.PlayerName}.Civ6Save";
            var dst     = Path.Combine(config.ActiveGameEntry!.SharedFolderPath, dstName);
            File.Copy(savePath, dst, overwrite: true);

            var hash = GameManifest.HashFile(dst);
            manifest.RecordSubmit(
                submittingPlayer: config.PlayerName!,
                submittedAtTurn:  realTurn,
                saveFile:         dstName,
                hash:             hash,
                nextTurn:         realTurn,
                nextPlayer:       nextPlayer);
            manifest.Save(config.ActiveGameEntry.SharedFolderPath);

            AnsiConsole.MarkupLine(
                $"[green]Submitted turn {realTurn} as[/] [grey]{dstName.EscapeMarkup()}[/]");
            AnsiConsole.MarkupLine($"Next up: [yellow]{manifest.CurrentPlayer}[/] (turn {manifest.CurrentTurn}).");

            // Backup retention: trim old per-turn .Civ6Save files in the
            // shared folder, keeping the N most recent.
            TrimOldSaves(config.ActiveGameEntry.SharedFolderPath, manifest, keepLast: 5);

            if (!string.IsNullOrEmpty(manifest.DiscordWebhookUrl))
            {
                var msg =
                    $"**{manifest.GameName}** — turn {realTurn} submitted by {config.PlayerName}. " +
                    $"It's now **{manifest.CurrentPlayer}**'s turn (T{manifest.CurrentTurn}).";
                var ok = DiscordWebhook.PostAsync(manifest.DiscordWebhookUrl, msg)
                    .GetAwaiter().GetResult();
                if (!ok)
                    AnsiConsole.MarkupLine("[grey]   (Discord webhook post failed; continuing.)[/]");
            }

            return 0;
        }
        finally
        {
            SubmitLock.Release(config.ActiveGameEntry!.SharedFolderPath);
        }
    }

    private static void PrintConflict(SubmitConflict c)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[yellow]⚠  {c.Title}[/]");
        AnsiConsole.MarkupLine($"   {c.Detail}");
        AnsiConsole.MarkupLine($"   [bold]How to fix:[/] {c.Remediation}");
    }

    /// <summary>
    /// Map the active player at save time (from Lua.log, where the value is the
    /// Civ "player name" string) to one of the manifest's player identifiers,
    /// case-insensitively. Falls back to next-in-list if no match.
    /// </summary>
    private static string ResolveNextPlayer(string activePlayerAtSave, List<string> manifestPlayers)
    {
        var match = manifestPlayers.FirstOrDefault(
            p => p.Equals(activePlayerAtSave, StringComparison.OrdinalIgnoreCase));
        return match ?? manifestPlayers[0];
    }

    private static void TrimOldSaves(string sharedFolder, GameManifest manifest, int keepLast)
    {
        try
        {
            // Identify files referenced by the manifest's history (we never
            // delete those — leave the rolling-history reference window intact).
            var referenced = manifest.History
                .Reverse<GameManifest.HistoryEntry>()
                .Take(keepLast)
                .Select(h => h.SavedAs)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (manifest.LatestSaveFile is not null)
                referenced.Add(manifest.LatestSaveFile);

            var dir = new DirectoryInfo(sharedFolder);
            foreach (var file in dir.EnumerateFiles($"{manifest.GameName}_T*.Civ6Save"))
            {
                if (!referenced.Contains(file.Name))
                {
                    try { file.Delete(); } catch { /* best-effort */ }
                }
            }
        }
        catch { }
    }
}
