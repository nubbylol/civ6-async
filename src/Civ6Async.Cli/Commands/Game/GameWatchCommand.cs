using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

/// <summary>
/// Long-running background watcher. Two FileSystemWatchers fire when:
///   - a new save appears in the local Civ Hotseat saves folder (the user
///     just played their turn) → desktop notification "ready to submit?"
///   - turn_state.json in the shared folder is modified by another helper
///     (someone else just submitted) → if it's now your turn, notify.
/// Console keeps printing a status line so you know the watcher is alive.
/// Ctrl+C exits cleanly.
/// </summary>
internal sealed class GameWatchCommand : Command<EmptySettings>
{
    protected override int Execute(CommandContext context, EmptySettings settings, CancellationToken cancellationToken)
    {
        var (config, manifest, err) = GameContext.Resolve();
        if (err is not null) { AnsiConsole.MarkupLine(err); return 1; }

        var savesDir = PlatformPaths.AutoDetectHotseatSavesDir();
        if (savesDir is null)
        {
            AnsiConsole.MarkupLine("[red]Civ 6 hotseat saves folder not found.[/]");
            return 1;
        }
        Directory.CreateDirectory(savesDir);

        var sharedDir = config!.ActiveGameEntry!.SharedFolderPath;
        var me        = config.PlayerName!;

        var luaLogPath = PlatformPaths.AutoDetectLuaLogPath();

        AnsiConsole.MarkupLine($"[green]Watching[/] [bold]{manifest!.GameName}[/] as [bold]{me}[/].");
        AnsiConsole.MarkupLine($"  Local saves:  [grey]{savesDir.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"  Shared:       [grey]{sharedDir.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"  Lua.log:      [grey]{(luaLogPath ?? "(not found)").EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine("[grey]Press Ctrl+C to stop.[/]");
        AnsiConsole.WriteLine();
        PrintStatus(manifest, me);

        using var stop      = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

        // Track which manifest state we last announced so the shared-folder
        // watcher doesn't double-fire on its own writes.
        var lastSeenTurn      = manifest.CurrentTurn;
        var lastSeenPlayer    = manifest.CurrentPlayer;
        var lastSavesScan     = DateTimeOffset.UtcNow;
        var lastLogScan       = DateTimeOffset.UtcNow.AddDays(-1);  // force first read
        var announcedDivergeAt = -1;                                 // prevents repeat warnings

        using var savesWatcher = new FileSystemWatcher(savesDir, "*.Civ6Save")
        {
            NotifyFilter         = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents  = true,
        };
        savesWatcher.Created += (_, e) => OnNewLocalSave(e.FullPath);
        savesWatcher.Changed += (_, e) => OnNewLocalSave(e.FullPath);

        using var sharedWatcher = new FileSystemWatcher(sharedDir, GameManifest.FileName)
        {
            NotifyFilter         = NotifyFilters.LastWrite,
            EnableRaisingEvents  = true,
        };
        sharedWatcher.Changed += (_, _) => OnManifestChanged();

        FileSystemWatcher? logWatcher = null;
        if (luaLogPath is not null)
        {
            var logDir  = Path.GetDirectoryName(luaLogPath)!;
            var logFile = Path.GetFileName(luaLogPath);
            logWatcher = new FileSystemWatcher(logDir, logFile)
            {
                NotifyFilter        = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            logWatcher.Changed += (_, _) => OnLuaLogChanged();
        }
        using var _logWatcherDisposable = logWatcher;

        // Block until Ctrl+C.
        try { stop.Token.WaitHandle.WaitOne(); }
        catch (OperationCanceledException) { }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Watcher stopped.[/]");
        return 0;

        // ---------- handlers (closures so they share state) ----------

        void OnNewLocalSave(string path)
        {
            // Debounce: Civ writes the file in chunks; multiple events fire.
            var now = DateTimeOffset.UtcNow;
            if ((now - lastSavesScan).TotalSeconds < 1) return;
            lastSavesScan = now;

            // Skip files we wrote during 'check' (civ6-async-* prefix).
            if (Path.GetFileName(path).StartsWith(SavePicker.DownloadedSavePrefix, StringComparison.OrdinalIgnoreCase))
                return;

            // Reload the manifest fresh — it might have advanced since we
            // started watching (e.g. another helper just submitted).
            var current = GameManifest.TryLoad(sharedDir);
            if (current is null) return;

            if (!string.Equals(current.CurrentPlayer, me, StringComparison.OrdinalIgnoreCase))
            {
                // Not our turn — user might be playing solo or messing about
                // outside the shared game. Stay quiet.
                return;
            }

            // Tiny grace pause: Civ may still be flushing the file when the
            // FileSystemWatcher fires. SubmitFlow's File.Copy will fail if the
            // file is still open exclusively. 250ms is empirically enough.
            Thread.Sleep(250);

            var name = Path.GetFileName(path);
            AnsiConsole.MarkupLine($"[green]►[/] Civ saved [grey]{name.EscapeMarkup()}[/] — auto-submitting…");
            Beep();

            var result = SubmitFlow.Run(config!, current, path, force: false);
            if (result.Outcome == SubmitFlow.Outcome.Submitted)
            {
                lastSeenTurn   = current.CurrentTurn;   // updated by RecordSubmit
                lastSeenPlayer = current.CurrentPlayer;
            }
            // BlockedByConflict / BlockedByLock / etc. already printed their
            // own diagnostics via SubmitFlow. Keep watching.
        }

        void OnLuaLogChanged()
        {
            // Civ writes the log very frequently; debounce to once a second.
            var now = DateTimeOffset.UtcNow;
            if ((now - lastLogScan).TotalSeconds < 1) return;
            lastLogScan = now;

            // Most recent save_complete from civ6-async events.
            var ev = LuaLogReader.FindLatest(new[] { "save_complete" }, luaLogPath);
            if (ev is null) return;

            var current = GameManifest.TryLoad(sharedDir);
            if (current is null) return;

            // Divergence: in-game turn is ahead of what the manifest thinks.
            // If you've played and saved but not submitted, this fires.
            if (ev.Turn is int liveTurn
                && liveTurn > current.CurrentTurn
                && liveTurn != announcedDivergeAt)
            {
                announcedDivergeAt = liveTurn;
                AnsiConsole.MarkupLine(
                    $"[yellow]►[/] In-game turn is [bold]{liveTurn}[/] but the shared manifest is on " +
                    $"[bold]{current.CurrentTurn}[/]. " +
                    "Run [bold]civ6-async game submit[/] when ready.");
                Beep();
            }
        }

        void OnManifestChanged()
        {
            // Cloud-sync clients can fire multiple writes; debounce briefly.
            Thread.Sleep(150);

            var current = GameManifest.TryLoad(sharedDir);
            if (current is null) return;

            var changed = current.CurrentTurn   != lastSeenTurn
                       || current.CurrentPlayer != lastSeenPlayer;
            if (!changed) return;

            lastSeenTurn   = current.CurrentTurn;
            lastSeenPlayer = current.CurrentPlayer;

            if (string.Equals(current.CurrentPlayer, me, StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine(
                    $"[green]►[/] [bold]Your turn[/] — turn {current.CurrentTurn}. " +
                    "Run [bold]civ6-async game check[/].");
                Beep();
            }
            else
            {
                AnsiConsole.MarkupLine(
                    $"[grey]►[/] Now waiting on [yellow]{current.CurrentPlayer}[/] " +
                    $"(turn {current.CurrentTurn}).");
            }
        }
    }

    private static void PrintStatus(GameManifest manifest, string me)
    {
        var iAmUp = string.Equals(manifest.CurrentPlayer, me, StringComparison.OrdinalIgnoreCase);
        AnsiConsole.MarkupLine(iAmUp
            ? $"[green]Your turn[/] (turn {manifest.CurrentTurn})."
            : $"Waiting on [yellow]{manifest.CurrentPlayer}[/] (turn {manifest.CurrentTurn}).");
    }

    private static void Beep()
    {
        try
        {
            if (OperatingSystem.IsWindows()) Console.Beep();
            else                              Console.Write('\a');
        }
        catch { }
    }
}
