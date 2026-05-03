using Civ6Async.Cli.Services;
using Civ6Async.Cli.Services.Storage;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace Civ6Async.Cli.Commands.Game;

/// <summary>
/// Sync mode — the day-one user-facing command. Combines the old separate
/// "whose turn?" + "watch" actions:
///
///   * Runs an immediate status check on entry. If it's your turn, drives
///     the same download flow GameStatusCommand uses, then watches the
///     local Civ saves folder for an end-of-turn save and auto-submits.
///   * If it's not your turn, shows status and a visible countdown to the
///     next manifest poll. While in this mode the only API traffic is one
///     request every PollSeconds.
///   * No polling at all while it IS your turn — the manifest can't move
///     without YOUR submit, and submit is event-driven via FileSystemWatcher.
/// </summary>
internal sealed class GameWatchCommand : Command<EmptySettings>
{
    private const int PollSeconds = 60;

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

        // Header (printed once, then we hand the bottom of the screen over to
        // the live region for status + countdown).
        AnsiConsole.MarkupLine($"[bold green]Sync[/] — game [bold]{manifest.GameName.EscapeMarkup()}[/], you are [bold]{me.EscapeMarkup()}[/].");
        AnsiConsole.MarkupLine($"  Storage:     [grey]{storage.Description.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"  Local saves: [grey]{savesDir.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine("[grey]Press Ctrl+C to stop.[/]");
        AnsiConsole.WriteLine();

        // If it's our turn at startup, run the smart download flow up front.
        // (Mirrors GameStatusCommand's behaviour so the user doesn't need a
        // separate "whose turn?" action.)
        if (string.Equals(manifest.CurrentPlayer, me, StringComparison.OrdinalIgnoreCase))
        {
            HandleMyTurnAtStartup(storage, manifest);
        }

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

        var lastSeenTurn        = manifest.CurrentTurn;
        var lastSeenPlayer      = manifest.CurrentPlayer;
        var lastSavesEvent      = DateTimeOffset.UtcNow.AddSeconds(-2);
        var lastLogEvent        = DateTimeOffset.UtcNow.AddDays(-1);
        var announcedDivergeAt  = -1;
        var lastPollAt          = DateTimeOffset.UtcNow;

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

        // Live region: refreshes every second so the countdown ticks. Anything
        // we MarkupLine elsewhere (events, submit results) prints ABOVE this
        // region — Spectre handles that automatically.
        AnsiConsole.Live(BuildStatusLine(lastSeenPlayer, lastSeenTurn, me, NextPollIn(lastPollAt)))
            .AutoClear(false)
            .Start(liveCtx =>
            {
                while (!stop.IsCancellationRequested)
                {
                    var iAmUp     = string.Equals(lastSeenPlayer, me, StringComparison.OrdinalIgnoreCase);
                    var remaining = NextPollIn(lastPollAt);

                    // Time to poll? (only if NOT our turn — we never poll our own turn)
                    if (!iAmUp && remaining <= 0)
                    {
                        DoPoll();
                        lastPollAt = DateTimeOffset.UtcNow;
                        remaining  = PollSeconds;
                    }

                    liveCtx.UpdateTarget(BuildStatusLine(lastSeenPlayer, lastSeenTurn, me, remaining));
                    Thread.Sleep(1000);
                }
            });

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Sync stopped.[/]");
        return 0;

        // ---------- handlers (closures share state) ----------

        int NextPollIn(DateTimeOffset since) =>
            Math.Max(0, PollSeconds - (int)(DateTimeOffset.UtcNow - since).TotalSeconds);

        void OnNewLocalSave(string path)
        {
            var now = DateTimeOffset.UtcNow;
            if ((now - lastSavesEvent).TotalSeconds < 1) return;
            lastSavesEvent = now;

            if (Path.GetFileName(path).StartsWith(SavePicker.DownloadedSavePrefix, StringComparison.OrdinalIgnoreCase))
                return;

            var current = GameManifest.TryLoad(storage);
            if (current is null) return;
            if (!string.Equals(current.CurrentPlayer, me, StringComparison.OrdinalIgnoreCase)) return;

            Thread.Sleep(250);  // grace pause; Civ may still be flushing.

            var name = Path.GetFileName(path);
            AnsiConsole.MarkupLine($"[green]►[/] Civ saved [grey]{name.EscapeMarkup()}[/] — auto-submitting…");
            Beep();

            var result = SubmitFlow.Run(config, storage, current, path, force: false);
            if (result.Outcome == SubmitFlow.Outcome.Submitted)
            {
                lastSeenTurn   = current.CurrentTurn;
                lastSeenPlayer = current.CurrentPlayer;
                // We just handed over — start the next poll cycle now.
                lastPollAt = DateTimeOffset.UtcNow;
            }
        }

        void OnLuaLogChanged()
        {
            var now = DateTimeOffset.UtcNow;
            if ((now - lastLogEvent).TotalSeconds < 1) return;
            lastLogEvent = now;

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
                    $"[yellow]►[/] In-game turn is [bold]{liveTurn}[/] but shared manifest is on " +
                    $"[bold]{current.CurrentTurn}[/]. Submit when ready.");
                Beep();
            }
        }

        void DoPoll()
        {
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
                        $"[green]►[/] [bold]Your turn[/] — turn {current.CurrentTurn}.");
                    Beep();
                    HandleMyTurnAtStartup(storage, current);
                }
                else
                {
                    AnsiConsole.MarkupLine(
                        $"[grey]►[/] Now waiting on [yellow]{current.CurrentPlayer.EscapeMarkup()}[/] " +
                        $"(turn {current.CurrentTurn}).");
                }
            }
            catch
            {
                // Silent — next tick retries.
            }
        }
    }

    /// <summary>
    /// On entry / on a freshly-detected my-turn handoff: download the latest
    /// save into the local Civ saves folder if needed and tell the user
    /// which file to load. Mirrors GameStatusCommand's smart behaviour.
    /// </summary>
    private static void HandleMyTurnAtStartup(IGameStorage storage, GameManifest manifest)
    {
        var plan = SaveDownloader.Inspect(storage, manifest);
        switch (plan.Status)
        {
            case SaveDownloader.Status.NoSaveYet:
                AnsiConsole.MarkupLine(
                    "It's [green]your turn[/] — turn 1. Play your first turn in Civ from a fresh hotseat game; " +
                    "Sync will auto-submit when you save.");
                break;

            case SaveDownloader.Status.SavesDirMissing:
                AnsiConsole.MarkupLine(
                    "It's [green]your turn[/], but Civ 6's saves folder wasn't found. Has Civ been launched here yet?");
                break;

            case SaveDownloader.Status.SourceMissing:
                AnsiConsole.MarkupLine(
                    "It's [green]your turn[/], but the latest save hasn't appeared in storage yet. " +
                    "Sync will retry; check back in a moment.");
                break;

            case SaveDownloader.Status.AlreadyHave:
                AnsiConsole.MarkupLine(
                    $"It's [green]your turn[/]. Latest save is already on this machine as " +
                    $"[bold]{plan.DestName!.EscapeMarkup()}[/]. Open it in Civ to play.");
                break;

            case SaveDownloader.Status.Stale:
                SaveDownloader.Execute(storage, plan);
                AnsiConsole.MarkupLine(
                    $"It's [green]your turn[/] (turn {manifest.CurrentTurn}). " +
                    $"Downloaded [bold]{plan.DestName!.EscapeMarkup()}[/] — open it in Civ to play.");
                break;
        }
    }

    private static IRenderable BuildStatusLine(string currentPlayer, int currentTurn, string me, int secondsToNextPoll)
    {
        var iAmUp = string.Equals(currentPlayer, me, StringComparison.OrdinalIgnoreCase);
        if (iAmUp)
        {
            return new Markup(
                $"[green]●[/] [bold]Your turn[/] (T{currentTurn}). Open the [bold]civ6-async-…[/] save in Civ. " +
                "[grey]Sync will auto-submit when you save.[/]");
        }
        return new Markup(
            $"[yellow]●[/] Waiting on [bold]{currentPlayer.EscapeMarkup()}[/] (T{currentTurn}). " +
            $"[grey]Next check in {secondsToNextPoll}s…[/]");
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
