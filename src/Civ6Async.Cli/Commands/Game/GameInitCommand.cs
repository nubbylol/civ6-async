using System.ComponentModel;
using Civ6Async.Cli.Services;
using Civ6Async.Cli.Services.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

internal sealed class GameInitCommand : Command<GameInitCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("Name for the new game.")]
        public required string Name { get; init; }

        [CommandOption("--provider <PROVIDER>")]
        [Description("Storage provider: local (default) or dropbox.")]
        public string Provider { get; init; } = "local";

        [CommandOption("--shared <PATH>")]
        [Description("when --provider=local: Path to the cloud-synced shared folder root (each game gets a subfolder).")]
        public string? SharedRoot { get; init; }

        [CommandOption("--dropbox-token <TOKEN>")]
        [Description("when --provider=dropbox: Dropbox access token (from your app at dropbox.com/developers/apps).")]
        public string? DropboxToken { get; init; }

        [CommandOption("--dropbox-folder <PATH>")]
        [Description("when --provider=dropbox: Dropbox folder root (default: App folder root).")]
        public string DropboxFolder { get; init; } = "";

        [CommandOption("--players <CSV>")]
        [Description("Comma-separated turn order, e.g. \"arin,max,jess\".")]
        public required string PlayersCsv { get; init; }

        [CommandOption("--me <NAME>")]
        [Description("Which player you are. Must be one of --players.")]
        public required string Me { get; init; }

        [CommandOption("--webhook <URL>")]
        [Description("Optional Discord webhook URL — submits will post to this channel.")]
        public string? Webhook { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var players = settings.PlayersCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (players.Count < 2)
        {
            AnsiConsole.MarkupLine("[red]Need at least 2 players in --players.[/]");
            return 1;
        }
        if (!players.Contains(settings.Me, StringComparer.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine($"[red]--me '{settings.Me}' must be one of: {string.Join(", ", players)}[/]");
            return 1;
        }

        if (settings.Webhook is not null && !DiscordWebhook.LooksValid(settings.Webhook))
        {
            AnsiConsole.MarkupLine("[red]--webhook doesn't look like a Discord webhook URL.[/]");
            return 1;
        }

        IGameStorage    storage;
        string          providerLabel;
        Action<LocalConfig> register;

        if (settings.Provider.Equals("dropbox", StringComparison.OrdinalIgnoreCase))
        {
            // Token: prefer the explicit --dropbox-token; otherwise fall back
            // to the per-machine saved token from a previous game.
            var existingConfig = LocalConfig.Load();
            var token = !string.IsNullOrEmpty(settings.DropboxToken)
                ? settings.DropboxToken
                : existingConfig.DropboxToken;
            if (string.IsNullOrEmpty(token))
            {
                AnsiConsole.MarkupLine(
                    "[red]No Dropbox token available.[/] Pass [bold]--dropbox-token[/] " +
                    "or save one via [bold]civ6-async defaults[/].");
                return 1;
            }

            var basePath = settings.DropboxFolder.TrimEnd('/') + "/" + settings.Name;
            var dropbox  = new DropboxStorage(token, basePath);

            string? verify = null;
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start("Verifying Dropbox access…", _ => verify = dropbox.VerifyAccess());
            if (verify is not null)
            {
                AnsiConsole.MarkupLine($"[red]Dropbox access check failed:[/] {verify.EscapeMarkup()}");
                return 1;
            }

            if (dropbox.Exists(GameManifest.FileName))
            {
                AnsiConsole.MarkupLine(
                    $"[red]A game already exists at[/] [grey]{basePath.EscapeMarkup()}[/]. " +
                    "Use [bold]game join[/] instead, or pick a different name.");
                return 1;
            }

            storage       = dropbox;
            providerLabel = $"Dropbox: {basePath}";
            register      = c => { c.DropboxToken = token; c.RegisterAndActivateDropbox(settings.Name, basePath); };
        }
        else
        {
            if (string.IsNullOrEmpty(settings.SharedRoot))
            {
                AnsiConsole.MarkupLine("[red]--shared is required for --provider local.[/]");
                return 1;
            }

            var sharedFolder = Path.Combine(settings.SharedRoot, settings.Name);
            if (Directory.Exists(sharedFolder)
                && File.Exists(Path.Combine(sharedFolder, GameManifest.FileName)))
            {
                AnsiConsole.MarkupLine(
                    $"[red]A game already exists at[/] [grey]{sharedFolder.EscapeMarkup()}[/]. " +
                    "Use [bold]game join[/] instead, or pick a different name.");
                return 1;
            }

            storage       = new LocalFolderStorage(sharedFolder);
            providerLabel = $"local folder: {sharedFolder}";
            register      = c => c.RegisterAndActivate(settings.Name, sharedFolder);
        }

        var manifest = new GameManifest
        {
            GameName          = settings.Name,
            CreatedAt         = DateTime.UtcNow,
            Players           = players,
            CurrentPlayer     = players[0],
            CurrentTurn       = 1,
            DiscordWebhookUrl = settings.Webhook,
        };
        manifest.Save(storage);

        var config = LocalConfig.Load();
        config.PlayerName = settings.Me;
        register(config);
        config.Save();

        AnsiConsole.MarkupLine($"[green]Created game[/] [bold]{settings.Name}[/]");
        AnsiConsole.MarkupLine($"  Storage:    [grey]{providerLabel.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"  Players:    {string.Join(" → ", players)}");
        AnsiConsole.MarkupLine($"  First turn: [bold]{players[0]}[/]");
        AnsiConsole.MarkupLine($"  You:        [bold]{settings.Me}[/]");
        AnsiConsole.WriteLine();

        ModBootstrap.EnsureInstalled();
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine(
            settings.Me.Equals(players[0], StringComparison.OrdinalIgnoreCase)
                ? "It's [green]your turn[/] first. Play in Civ, save, then run [bold]civ6-async game submit[/]."
                : $"Waiting on [yellow]{players[0]}[/] to play turn 1.");

        return 0;
    }
}
