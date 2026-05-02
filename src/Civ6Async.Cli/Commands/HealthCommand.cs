using System.ComponentModel;
using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands;

/// <summary>
/// Multi-check report. Each row is independent; "FAIL" means address that
/// check, but doesn't block the others from being shown.
/// </summary>
internal sealed class HealthCommand : Command<HealthCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--mods-dir <PATH>")]
        [Description("Override Civilization VI's Mods directory. Auto-detected if omitted.")]
        public string? ModsDir { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var table = new Table()
            .AddColumns("Check", "Result", "Detail")
            .Border(TableBorder.Rounded);

        var modsDir = settings.ModsDir ?? PlatformPaths.AutoDetectModsDir();

        // 1. Civ user folder detected
        if (modsDir is null)
        {
            table.AddRow("Civ 6 user folder", "[red]FAIL[/]", "Auto-detect found nothing. Run with --mods-dir.");
            AnsiConsole.Write(table);
            return 1;
        }
        table.AddRow("Civ 6 user folder", "[green]OK[/]", modsDir.EscapeMarkup());

        // 2. Mods directory writable
        var writable = IsWritable(modsDir);
        table.AddRow("Mods folder writable", writable ? "[green]OK[/]" : "[red]FAIL[/]",
            writable ? "" : "Permission denied / read-only.");

        // 3. Mod present
        var installed = ModInstaller.IsInstalled(modsDir);
        table.AddRow("Mod installed", installed ? "[green]OK[/]" : "[yellow]MISSING[/]",
            installed ? ModInstaller.GetInstallDir(modsDir).EscapeMarkup() : "Run [bold]civ6-async install[/].");

        // 4. File integrity (only if installed)
        if (installed)
        {
            var diffs = ModInstaller.VerifyIntegrity(modsDir);
            if (diffs is null)
                table.AddRow("File integrity", "[green]OK[/]", $"{EmbeddedMod.Files.Count} files match the embedded copy.");
            else
                table.AddRow("File integrity", "[yellow]DIVERGED[/]",
                    string.Join(", ", diffs.Take(3).Select(s => s.EscapeMarkup()))
                    + (diffs.Count > 3 ? $" (+{diffs.Count - 3} more)" : ""));
        }

        AnsiConsole.Write(table);

        // Exit code: zero if everything ok or only "DIVERGED"/"MISSING" warnings;
        // non-zero only when something fundamental is broken.
        return modsDir is null || !writable ? 1 : 0;
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".civ6-async-write-probe");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
