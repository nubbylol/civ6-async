using System.ComponentModel;
using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands;

internal sealed class InstallCommand : Command<InstallCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--mods-dir <PATH>")]
        [Description("Override Civilization VI's Mods directory. Auto-detected if omitted.")]
        public string? ModsDir { get; init; }

        [CommandOption("-y|--yes")]
        [Description("Skip the confirmation prompt.")]
        public bool Yes { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var modsDir = ResolveModsDir(settings.ModsDir);
        if (modsDir is null) return 1;

        var installDir = ModInstaller.GetInstallDir(modsDir);
        var alreadyInstalled = ModInstaller.IsInstalled(modsDir);

        AnsiConsole.MarkupLine($"Mods directory: [grey]{modsDir.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"Target:         [grey]{installDir.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine(alreadyInstalled
            ? "Status:         [yellow]existing install will be overwritten[/]"
            : "Status:         [green]fresh install[/]");
        AnsiConsole.WriteLine();

        if (!settings.Yes && !AnsiConsole.Confirm("Proceed?"))
        {
            AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
            return 1;
        }

        try
        {
            ModInstaller.Install(modsDir);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Install failed:[/] {ex.Message.EscapeMarkup()}");
            return 1;
        }

        AnsiConsole.MarkupLine("[green]Done.[/] Enable [bold]civ6-async[/] in Civilization VI's Additional Content → Mods menu.");
        return 0;
    }

    internal static string? ResolveModsDir(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            if (!Directory.Exists(overridePath))
            {
                AnsiConsole.MarkupLine($"[red]The path '{overridePath.EscapeMarkup()}' does not exist.[/]");
                return null;
            }
            return overridePath;
        }

        var detected = PlatformPaths.AutoDetectModsDir();
        if (detected is null)
        {
            AnsiConsole.MarkupLine("[red]Could not auto-detect Civilization VI's user folder.[/]");
            AnsiConsole.MarkupLine("Pass [yellow]--mods-dir <path>[/] explicitly. Tried:");
            foreach (var c in PlatformPaths.CandidateModsDirs())
                AnsiConsole.MarkupLine($"  • [grey]{c.EscapeMarkup()}[/]");
            return null;
        }

        // The Mods subfolder doesn't always exist on a brand-new install — Civ
        // creates it on first launch. Create it ourselves if missing so the
        // user doesn't have to launch the game just to install a mod.
        Directory.CreateDirectory(detected);
        return detected;
    }
}
