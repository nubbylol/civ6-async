using System.ComponentModel;
using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands;

internal sealed class StatusCommand : Command<StatusCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--mods-dir <PATH>")]
        [Description("Override Civilization VI's Mods directory. Auto-detected if omitted.")]
        public string? ModsDir { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var modsDir = !string.IsNullOrWhiteSpace(settings.ModsDir)
            ? settings.ModsDir
            : PlatformPaths.AutoDetectModsDir();

        if (modsDir is null)
        {
            AnsiConsole.MarkupLine("[red]Civilization VI Mods folder not found.[/]");
            return 1;
        }

        if (ModInstaller.IsInstalled(modsDir))
        {
            var installDir = ModInstaller.GetInstallDir(modsDir);
            AnsiConsole.MarkupLine($"[green]Installed[/] at [grey]{installDir.EscapeMarkup()}[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"[yellow]Not installed[/] (Mods folder: [grey]{modsDir.EscapeMarkup()}[/])");
        return 0;
    }
}
