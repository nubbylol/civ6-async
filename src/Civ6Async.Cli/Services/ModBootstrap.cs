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
            AnsiConsole.MarkupLine(
                "[green]Mod installed.[/] Don't forget to tick [bold]civ6-async[/] in " +
                "Civ → [bold]Additional Content → Mods[/].");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Mod install failed:[/] {ex.Message.EscapeMarkup()}");
            AnsiConsole.MarkupLine(
                "Run [bold]civ6-async install[/] manually to retry, or pass " +
                "[bold]--mods-dir <path>[/] if your Civ install is in a non-standard location.");
        }
    }
}
