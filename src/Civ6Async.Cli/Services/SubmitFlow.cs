using Spectre.Console;

namespace Civ6Async.Cli.Services;

/// <summary>
/// Shared submit pipeline used by both the explicit 'game submit' command
/// and the 'game watch' auto-submit-on-save path. Returns a structured
/// result so callers can decide how to react (continue watching vs exit
/// the CLI). Conflict warnings are printed inline; this method assumes
/// stdout already belongs to the user.
/// </summary>
internal static class SubmitFlow
{
    public enum Outcome
    {
        Submitted,
        BlockedByConflict,
        BlockedByLock,
        FileMissing,
        IoError,
    }

    public sealed record Result(Outcome Outcome, GameManifest? UpdatedManifest, int? SubmittedTurn);

    public static Result Run(LocalConfig config, GameManifest manifest, string savePath, bool force)
    {
        if (!File.Exists(savePath))
        {
            AnsiConsole.MarkupLine($"[red]Save file not found:[/] [grey]{savePath.EscapeMarkup()}[/]");
            return new Result(Outcome.FileMissing, null, null);
        }

        var conflicts = ConflictDetector.Detect(manifest, config.PlayerName!, savePath);
        if (conflicts.Count > 0)
        {
            foreach (var c in conflicts) PrintConflict(c);
            if (!force)
            {
                AnsiConsole.MarkupLine("\n[red]Submit refused.[/] Re-run with [bold]--force[/] to override.");
                return new Result(Outcome.BlockedByConflict, null, null);
            }
            AnsiConsole.MarkupLine("\n[yellow]--force given; submitting anyway.[/]");
        }

        if (!SubmitLock.TryAcquire(config.ActiveGameEntry!.SharedFolderPath, config.PlayerName!, out var blocking))
        {
            AnsiConsole.MarkupLine(
                $"[red]Another submit is already in progress.[/] " +
                $"Held by [yellow]{blocking!.Player}[/] on [yellow]{blocking.Hostname}[/] since " +
                $"[grey]{blocking.AcquiredAt:yyyy-MM-dd HH:mm:ss}[/] UTC.");
            return new Result(Outcome.BlockedByLock, null, null);
        }

        try
        {
            // Pull the actual game state from the most recent EventLogger
            // save_complete event — source of truth even if the in-game mod
            // auto-cycled through several players' turns.
            var saveEvent = LuaLogReader.FindLatest(new[] { "save_complete" });

            int    realTurn;
            string nextPlayer;

            if (saveEvent is { Turn: int t, Player: { } p })
            {
                realTurn   = t;
                nextPlayer = ResolveNextPlayer(p, manifest.Players);
                AnsiConsole.MarkupLine($"[grey]Lua.log says save was at turn {t}, active player {p}.[/]");
            }
            else
            {
                realTurn   = manifest.CurrentTurn;
                nextPlayer = manifest.Players[(manifest.Players.IndexOf(config.PlayerName!) + 1) % manifest.Players.Count];
                AnsiConsole.MarkupLine(
                    "[yellow]Warning:[/] no civ6-async save event found in Lua.log. " +
                    "Submitting with assumed turn/player advance — verify state after.");
            }

            var dstName = $"{manifest.GameName}_T{realTurn:D3}_{config.PlayerName}.Civ6Save";
            var dst     = Path.Combine(config.ActiveGameEntry.SharedFolderPath, dstName);
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

            AnsiConsole.MarkupLine($"[green]Submitted turn {realTurn} as[/] [grey]{dstName.EscapeMarkup()}[/]");
            AnsiConsole.MarkupLine($"Next up: [yellow]{manifest.CurrentPlayer}[/] (turn {manifest.CurrentTurn}).");

            TrimOldSaves(config.ActiveGameEntry.SharedFolderPath, manifest, keepLast: 5);

            if (!string.IsNullOrEmpty(manifest.DiscordWebhookUrl))
            {
                var msg =
                    $"**{manifest.GameName}** — turn {realTurn} submitted by {config.PlayerName}. " +
                    $"It's now **{manifest.CurrentPlayer}**'s turn (T{manifest.CurrentTurn}).";
                var ok = DiscordWebhook.PostAsync(manifest.DiscordWebhookUrl, msg).GetAwaiter().GetResult();
                if (!ok)
                    AnsiConsole.MarkupLine("[grey]   (Discord webhook post failed; continuing.)[/]");
            }

            return new Result(Outcome.Submitted, manifest, realTurn);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Submit failed:[/] {ex.Message.EscapeMarkup()}");
            return new Result(Outcome.IoError, null, null);
        }
        finally
        {
            SubmitLock.Release(config.ActiveGameEntry.SharedFolderPath);
        }
    }

    private static void PrintConflict(SubmitConflict c)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[yellow]⚠  {c.Title}[/]");
        AnsiConsole.MarkupLine($"   {c.Detail}");
        AnsiConsole.MarkupLine($"   [bold]How to fix:[/] {c.Remediation}");
    }

    private static string ResolveNextPlayer(string activePlayerAtSave, List<string> manifestPlayers)
    {
        var match = manifestPlayers.FirstOrDefault(p =>
            p.Equals(activePlayerAtSave, StringComparison.OrdinalIgnoreCase));
        return match ?? manifestPlayers[0];
    }

    private static void TrimOldSaves(string sharedFolder, GameManifest manifest, int keepLast)
    {
        try
        {
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
