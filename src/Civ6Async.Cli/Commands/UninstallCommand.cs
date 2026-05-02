using System.ComponentModel;
using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands;

internal sealed class UninstallCommand : Command<UninstallCommand.Settings>
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
        var modsDir = InstallCommand.ResolveModsDir(settings.ModsDir);
        if (modsDir is null) return 1;

        if (!ModInstaller.IsInstalled(modsDir))
        {
            AnsiConsole.MarkupLine("[yellow]civ6-async is not installed.[/] Nothing to do.");
            return 0;
        }

        var installDir = ModInstaller.GetInstallDir(modsDir);
        AnsiConsole.MarkupLine($"Will remove: [grey]{installDir.EscapeMarkup()}[/]");

        if (!settings.Yes && !AnsiConsole.Confirm("Proceed?"))
        {
            AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
            return 1;
        }

        try
        {
            ModInstaller.Uninstall(modsDir);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Uninstall failed:[/] {ex.Message.EscapeMarkup()}");
            return 1;
        }

        AnsiConsole.MarkupLine("[green]Removed.[/]");
        return 0;
    }
}
