using Spectre.Console;

namespace Civ6Async.Cli.Services;

/// <summary>
/// Convenience used by 'game init' / 'game join' so the user doesn't have
/// to know to run 'civ6-async install' separately. Auto-detects the Civ
/// Mods folder and installs the mod if it isn't already there. Logs what
/// it did. Soft-fails (warns) on auto-detect or copy failure rather than
/// blocking the surrounding init/join flow.
/// </summary>
internal static class ModBootstrap
{
    public static void EnsureInstalled()
    {
        var modsDir = PlatformPaths.AutoDetectModsDir();
        if (modsDir is null)
        {
            AnsiConsole.MarkupLine(
                "[yellow]Couldn't auto-detect Civ 6's Mods folder.[/] " +
                "Run [bold]civ6-async install --mods-dir <path>[/] manually before playing.");
            return;
        }

        if (ModInstaller.IsInstalled(modsDir))
        {
            AnsiConsole.MarkupLine(
                $"[grey]Mod already installed at {ModInstaller.GetInstallDir(modsDir).EscapeMarkup()}.[/]");
            return;
        }

        AnsiConsole.MarkupLine($"[grey]Installing mod into {modsDir.EscapeMarkup()}…[/]");
        try
        {
            ModInstaller.Install(modsDir);
            AnsiConsole.MarkupLine("[green]Mod installed.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Mod install failed:[/] {ex.Message.EscapeMarkup()}");
            AnsiConsole.MarkupLine(
                "Run [bold]civ6-async install[/] manually to retry, or pass " +
                "[bold]--mods-dir <path>[/] if your Civ install is in a non-standard location.");
            return;
        }

        TryAutoEnable();
    }

    private static void TryAutoEnable()
    {
        var (result, message) = ModEnabler.TryEnable();
        switch (result)
        {
            case ModEnabler.Result.Enabled:
                AnsiConsole.MarkupLine("[green]Mod enabled in Civ.[/] Just launch the game.");
                break;
            case ModEnabler.Result.AlreadyEnabled:
                AnsiConsole.MarkupLine("[grey]Mod already enabled in Civ.[/]");
                break;
            case ModEnabler.Result.ModNotInDb:
                AnsiConsole.MarkupLine(
                    "[yellow]Couldn't auto-enable yet:[/] Civ hasn't scanned the Mods folder. " +
                    "Launch Civ once (then exit), and re-run [bold]civ6-async install[/] to enable.");
                AnsiConsole.MarkupLine(
                    "[grey]Or just tick civ6-async manually in Civ → Additional Content → Mods this once.[/]");
                break;
            case ModEnabler.Result.DbBusy:
                AnsiConsole.MarkupLine(
                    $"[yellow]Couldn't auto-enable:[/] {message?.EscapeMarkup()} " +
                    "Re-run [bold]civ6-async install[/] after closing Civ.");
                break;
            case ModEnabler.Result.DbNotFound:
                AnsiConsole.MarkupLine(
                    "[grey]Mod database not found yet (you'll need to launch Civ once before auto-enable works). " +
                    "Tick civ6-async manually in Additional Content → Mods this time.[/]");
                break;
            default:
                AnsiConsole.MarkupLine(
                    $"[grey]Auto-enable skipped: {message?.EscapeMarkup() ?? "(unknown)"}. " +
                    "Tick civ6-async manually in Additional Content → Mods.[/]");
                break;
        }
    }
}
