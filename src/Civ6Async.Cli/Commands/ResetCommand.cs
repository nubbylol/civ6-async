using System.ComponentModel;
using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands;

/// <summary>
/// Wipe local civ6-async state on this machine without touching the
/// cloud-shared folder or its manifests / save files. Useful when:
///   - you want to start over with a different Drive root,
///   - you're handing the machine to someone else,
///   - the local config got corrupted.
///
/// Removes:
///   - %APPDATA%/civ6-async/config.json (player identity, joined-games list)
///   - civ6-async-*.Civ6Save files in the Civ Hotseat saves folder (the
///     turn downloads we wrote during 'check')
///
/// Does NOT touch:
///   - Anything in the shared cloud folder (turn_state.json, submitted saves)
///   - The installed mod files (use 'civ6-async uninstall' for that)
///   - Civ's own saves not authored by us
/// </summary>
internal sealed class ResetCommand : Command<ResetCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-y|--yes")]
        [Description("Skip the confirmation prompt.")]
        public bool Yes { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var configPath = LocalConfig.ConfigPath;
        var savesDir   = PlatformPaths.AutoDetectHotseatSavesDir();

        var deletableSaves = savesDir is not null && Directory.Exists(savesDir)
            ? new DirectoryInfo(savesDir)
                .EnumerateFiles($"{SavePicker.DownloadedSavePrefix}*.Civ6Save")
                .ToList()
            : new List<FileInfo>();

        AnsiConsole.MarkupLine("[bold]Reset will remove:[/]");
        AnsiConsole.MarkupLine($"  • Local config: [grey]{configPath.EscapeMarkup()}[/] " +
            (File.Exists(configPath) ? "" : "[grey](not present)[/]"));
        AnsiConsole.MarkupLine(deletableSaves.Count == 0
            ? "  • Downloaded turn saves: [grey](none)[/]"
            : $"  • {deletableSaves.Count} downloaded turn save(s) in [grey]{savesDir!.EscapeMarkup()}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Cloud-shared folders, the installed mod files, and any of your own[/]");
        AnsiConsole.MarkupLine("[grey]non-civ6-async-named saves are NOT affected.[/]");
        AnsiConsole.WriteLine();

        if (!settings.Yes && !AnsiConsole.Confirm("Proceed?"))
        {
            AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
            return 1;
        }

        var configDeleted = false;
        if (File.Exists(configPath))
        {
            try { File.Delete(configPath); configDeleted = true; }
            catch (Exception ex) { AnsiConsole.MarkupLine($"[red]Couldn't delete config:[/] {ex.Message.EscapeMarkup()}"); }
        }

        var savesDeleted = 0;
        foreach (var f in deletableSaves)
        {
            try { f.Delete(); savesDeleted++; }
            catch { /* skip files held by Civ etc. */ }
        }

        AnsiConsole.MarkupLine(
            $"[green]Reset complete.[/] " +
            $"Config: [bold]{(configDeleted ? "removed" : "—")}[/]. " +
            $"Saves: [bold]{savesDeleted}[/] removed.");
        AnsiConsole.MarkupLine(
            "Run [bold]civ6-async[/] again — the first-run wizard will set you up fresh.");
        return 0;
    }
}
