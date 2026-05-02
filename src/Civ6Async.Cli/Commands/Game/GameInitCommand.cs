using System.ComponentModel;
using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

internal sealed class GameInitCommand : Command<GameInitCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("Name for the new game (used as folder name + manifest field).")]
        public required string Name { get; init; }

        [CommandOption("--shared <PATH>")]
        [Description("Path to the cloud-synced shared folder (e.g. ~/Dropbox/civ6-async).")]
        public required string SharedRoot { get; init; }

        [CommandOption("--players <CSV>")]
        [Description("Comma-separated turn order, e.g. \"arin,max,jess\".")]
        public required string PlayersCsv { get; init; }

        [CommandOption("--me <NAME>")]
        [Description("Which player you are. Must be one of --players. Saved locally.")]
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

        var sharedFolder = Path.Combine(settings.SharedRoot, settings.Name);
        if (Directory.Exists(sharedFolder) &&
            File.Exists(GameManifest.ManifestPathIn(sharedFolder)))
        {
            AnsiConsole.MarkupLine(
                $"[red]A game already exists at[/] [grey]{sharedFolder.EscapeMarkup()}[/]. " +
                "Use [bold]game join[/] instead, or pick a different name.");
            return 1;
        }

        Directory.CreateDirectory(sharedFolder);

        if (settings.Webhook is not null && !DiscordWebhook.LooksValid(settings.Webhook))
        {
            AnsiConsole.MarkupLine(
                "[red]--webhook doesn't look like a Discord webhook URL.[/] " +
                "Expected: https://discord.com/api/webhooks/...");
            return 1;
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
        manifest.Save(sharedFolder);

        var config = LocalConfig.Load();
        config.PlayerName = settings.Me;
        config.RegisterAndActivate(settings.Name, sharedFolder);
        config.Save();

        AnsiConsole.MarkupLine($"[green]Created game[/] [bold]{settings.Name}[/]");
        AnsiConsole.MarkupLine($"  Shared folder: [grey]{sharedFolder.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"  Players:       {string.Join(" → ", players)}");
        AnsiConsole.MarkupLine($"  First turn:    [bold]{players[0]}[/]");
        AnsiConsole.MarkupLine($"  You:           [bold]{settings.Me}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            settings.Me.Equals(players[0], StringComparison.OrdinalIgnoreCase)
                ? "It's [green]your turn[/] first. Play in Civ, save, then run [bold]civ6-async game submit[/]."
                : $"Waiting on [yellow]{players[0]}[/] to play turn 1.");

        return 0;
    }
}
