using System.ComponentModel;
using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

internal sealed class GameJoinCommand : Command<GameJoinCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--shared <PATH>")]
        [Description("Path to the existing game's shared folder (containing turn_state.json).")]
        public required string SharedFolder { get; init; }

        [CommandOption("--me <NAME>")]
        [Description("Which player you are. Must be one of the manifest's players. Optional — picker if omitted.")]
        public string? Me { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var manifest = GameManifest.TryLoad(settings.SharedFolder);
        if (manifest is null)
        {
            AnsiConsole.MarkupLine(
                $"[red]No game manifest found at[/] [grey]{settings.SharedFolder.EscapeMarkup()}[/]. " +
                "Wait for the host to create the game with [bold]game init[/], " +
                "or check the path is right.");
            return 1;
        }

        var me = settings.Me ?? AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Which player are you?")
                .AddChoices(manifest.Players));

        if (!manifest.Players.Contains(me, StringComparer.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine(
                $"[red]'{me}' is not a player in this game.[/] Players: {string.Join(", ", manifest.Players)}");
            return 1;
        }

        var config = LocalConfig.Load();
        config.PlayerName = me;
        config.RegisterAndActivate(manifest.GameName, settings.SharedFolder);
        config.Save();

        AnsiConsole.MarkupLine($"[green]Joined[/] [bold]{manifest.GameName}[/] as [bold]{me}[/].");
        AnsiConsole.MarkupLine($"Currently on turn [bold]{manifest.CurrentTurn}[/], waiting on [yellow]{manifest.CurrentPlayer}[/].");
        AnsiConsole.WriteLine();

        ModBootstrap.EnsureInstalled();
        return 0;
    }
}
