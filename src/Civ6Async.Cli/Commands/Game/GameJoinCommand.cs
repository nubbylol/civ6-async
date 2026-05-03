using System.ComponentModel;
using Civ6Async.Cli.Services;
using Civ6Async.Cli.Services.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

internal sealed class GameJoinCommand : Command<GameJoinCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--shared <PATH>")]
        [Description("when --provider=local: Full path to the existing game's shared folder.")]
        public string? SharedFolder { get; init; }

        [CommandOption("--dropbox-token <TOKEN>")]
        [Description("when --provider=dropbox: Dropbox access token from the host.")]
        public string? DropboxToken { get; init; }

        [CommandOption("--dropbox-folder <PATH>")]
        [Description("when --provider=dropbox: Full Dropbox path of the game folder, e.g. /civ6-async/MyGame.")]
        public string? DropboxFolder { get; init; }

        [CommandOption("--me <NAME>")]
        [Description("Which player you are. Picker if omitted.")]
        public string? Me { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        IGameStorage storage;
        Action<LocalConfig, string> register;

        if (!string.IsNullOrEmpty(settings.DropboxToken) && !string.IsNullOrEmpty(settings.DropboxFolder))
        {
            var dropbox = new DropboxStorage(settings.DropboxToken, settings.DropboxFolder);
            var verify  = dropbox.VerifyAccess();
            if (verify is not null)
            {
                AnsiConsole.MarkupLine($"[red]Dropbox access check failed:[/] {verify.EscapeMarkup()}");
                return 1;
            }
            storage  = dropbox;
            register = (cfg, gameName) => cfg.RegisterAndActivateDropbox(gameName, settings.DropboxToken, settings.DropboxFolder);
        }
        else if (!string.IsNullOrEmpty(settings.SharedFolder))
        {
            storage  = new LocalFolderStorage(settings.SharedFolder);
            register = (cfg, gameName) => cfg.RegisterAndActivate(gameName, settings.SharedFolder);
        }
        else
        {
            AnsiConsole.MarkupLine("[red]Pass either --shared <path> (local) or --dropbox-token + --dropbox-folder.[/]");
            return 1;
        }

        var manifest = GameManifest.TryLoad(storage);
        if (manifest is null)
        {
            AnsiConsole.MarkupLine(
                $"[red]No game manifest found at[/] [grey]{storage.Description.EscapeMarkup()}[/]. " +
                "Wait for the host to create the game with [bold]game init[/], or check the path / token.");
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
        register(config, manifest.GameName);
        config.Save();

        AnsiConsole.MarkupLine($"[green]Joined[/] [bold]{manifest.GameName}[/] as [bold]{me}[/].");
        AnsiConsole.MarkupLine($"Currently on turn [bold]{manifest.CurrentTurn}[/], waiting on [yellow]{manifest.CurrentPlayer}[/].");
        AnsiConsole.WriteLine();

        ModBootstrap.EnsureInstalled();
        return 0;
    }
}
