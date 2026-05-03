using Civ6Async.Cli.Services;
using Civ6Async.Cli.Services.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

/// <summary>
/// Background watcher. Two trigger sources:
///   - Local Civ saves folder via FileSystemWatcher → auto-submit on save.
///   - Shared storage manifest via polling (every PollSeconds) → notify
///     when it's now your turn. (FileSystemWatcher works for local-folder
///     storage; for Dropbox we have to poll.)
///   - Lua.log via FileSystemWatcher → divergence detection.
/// </summary>
internal sealed class GameWatchCommand : Command<EmptySettings>
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);

    protected override int Execute(CommandContext context, EmptySettings settings, CancellationToken cancellationToken)
    {
        var (ctx, err) = GameContext.Resolve();
        if (err is not null) { AnsiConsole.MarkupLine(err); return 1; }
        var (config, storage, manifest) = (ctx!.Config, ctx.Storage, ctx.Manifest);

        var savesDir = PlatformPaths.AutoDetectHotseatSavesDir();
        if (savesDir is null)
        {
            AnsiConsole.MarkupLine("[red]Civ 6 hotseat saves folder not found.[/]");
            return 1;
        }
        Directory.CreateDirectory(savesDir);

        var luaLogPath = PlatformPaths.AutoDetectLuaLogPath();
        var me         = config.PlayerName!;

        AnsiConsole.MarkupLine($"[green]Watching[/] [bold]{manifest.GameName}[/] as [bold]{me}[/].");
        AnsiConsole.MarkupLine($"  Local saves: [grey]{savesDir.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"  Storage:     [grey]{storage.Description.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"  Lua.log:     [grey]{(luaLogPath ?? "(not found)").EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine("[grey]Press Ctrl+C to stop.[/]");
        AnsiConsole.WriteLine();
        PrintInitialStatus(manifest, me);

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

        var lastSeenTurn       = manifest.CurrentTurn;
        var lastSeenPlayer     = manifest.CurrentPlayer;
        var lastSavesScan      = DateTimeOffset.UtcNow;
        var lastLogScan        = DateTimeOffset.UtcNow.AddDays(-1);
        var announcedDivergeAt = -1;

        using var savesWatcher = new FileSystemWatcher(savesDir, "*.Civ6Save")
        {
            NotifyFilter        = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        savesWatcher.Created += (_, e) => OnNewLocalSave(e.FullPath);
        savesWatcher.Changed += (_, e) => OnNewLocalSave(e.FullPath);

        FileSystemWatcher? logWatcher = null;
        if (luaLogPath is not null)
        {
            logWatcher = new FileSystemWatcher(Path.GetDirectoryName(luaLogPath)!, Path.GetFileName(luaLogPath))
            {
                NotifyFilter        = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            logWatcher.Changed += (_, _) => OnLuaLogChanged();
        }
        using var _logWatcherDisposable = logWatcher;

        // Poll the shared storage on a timer for "now your turn" detection.
        // Works for both local-folder (cheap) and Dropbox (HTTPS round-trip).
        var pollTimer = new Timer(_ => PollManifest(), null, PollInterval, PollInterval);
        using var _timerDisposable = pollTimer;

        try { stop.Token.WaitHandle.WaitOne(); }
        catch (OperationCanceledException) { }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Watcher stopped.[/]");
        return 0;

        // ---------- handlers ----------

        void OnNewLocalSave(string path)
        {
            var now = DateTimeOffset.UtcNow;
            if ((now - lastSavesScan).TotalSeconds < 1) return;
            lastSavesScan = now;

            if (Path.GetFileName(path).StartsWith(SavePicker.DownloadedSavePrefix, StringComparison.OrdinalIgnoreCase))
                return;

            // Reload manifest fresh — might have advanced since startup.
            var current = GameManifest.TryLoad(storage);
            if (current is null) return;

            if (!string.Equals(current.CurrentPlayer, me, StringComparison.OrdinalIgnoreCase))
                return;

            Thread.Sleep(250);  // grace pause; Civ may still be flushing.

            var name = Path.GetFileName(path);
            AnsiConsole.MarkupLine($"[green]►[/] Civ saved [grey]{name.EscapeMarkup()}[/] — auto-submitting…");
            Beep();

            var result = SubmitFlow.Run(config, storage, current, path, force: false);
            if (result.Outcome == SubmitFlow.Outcome.Submitted)
            {
                lastSeenTurn   = current.CurrentTurn;
                lastSeenPlayer = current.CurrentPlayer;
            }
        }

        void OnLuaLogChanged()
        {
            var now = DateTimeOffset.UtcNow;
            if ((now - lastLogScan).TotalSeconds < 1) return;
            lastLogScan = now;

            var ev = LuaLogReader.FindLatest(new[] { "save_complete" }, luaLogPath);
            if (ev is null) return;

            var current = GameManifest.TryLoad(storage);
            if (current is null) return;

            if (ev.Turn is int liveTurn
                && liveTurn > current.CurrentTurn
                && liveTurn != announcedDivergeAt)
            {
                announcedDivergeAt = liveTurn;
                AnsiConsole.MarkupLine(
                    $"[yellow]►[/] In-game turn is [bold]{liveTurn}[/] but the shared manifest is on " +
                    $"[bold]{current.CurrentTurn}[/]. Submit when ready.");
                Beep();
            }
        }

        void PollManifest()
        {
            // No point hitting the API while it's our turn — the manifest can
            // only change as the result of OUR submit, and that's caught by
            // the local-save FileSystemWatcher, not by polling. We resume
            // polling automatically once submit advances lastSeenPlayer to
            // somebody else.
            if (string.Equals(lastSeenPlayer, me, StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                var current = GameManifest.TryLoad(storage);
                if (current is null) return;

                if (current.CurrentTurn == lastSeenTurn
                    && string.Equals(current.CurrentPlayer, lastSeenPlayer, StringComparison.OrdinalIgnoreCase))
                    return;

                lastSeenTurn   = current.CurrentTurn;
                lastSeenPlayer = current.CurrentPlayer;

                if (string.Equals(current.CurrentPlayer, me, StringComparison.OrdinalIgnoreCase))
                {
                    AnsiConsole.MarkupLine(
                        $"[green]►[/] [bold]Your turn[/] — turn {current.CurrentTurn}. " +
                        "Run [bold]civ6-async game check[/] to download (or pick \"Whose turn?\" in the menu).");
                    Beep();
                }
                else
                {
                    AnsiConsole.MarkupLine(
                        $"[grey]►[/] Now waiting on [yellow]{current.CurrentPlayer}[/] " +
                        $"(turn {current.CurrentTurn}).");
                }
            }
            catch
            {
                // Network blip / sync glitch — silent retry next interval.
            }
        }
    }

    private static void PrintInitialStatus(GameManifest manifest, string me)
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
