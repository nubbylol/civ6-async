using System.ComponentModel;
using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

/// <summary>
/// Set / clear the Discord webhook URL on the active game's manifest. Stored
/// in turn_state.json so every player's helper picks it up via cloud sync;
/// only the host (or whoever runs this) needs to know the URL.
/// </summary>
internal sealed class GameWebhookCommand : Command<GameWebhookCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[URL]")]
        [Description("Discord webhook URL. Omit to print the current value; pass 'clear' to remove.")]
        public string? Url { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (config, manifest, err) = GameContext.Resolve();
        if (err is not null) { AnsiConsole.MarkupLine(err); return 1; }

        if (settings.Url is null)
        {
            AnsiConsole.MarkupLine(
                manifest!.DiscordWebhookUrl is null
                    ? "[grey]No webhook configured.[/]"
                    : $"[grey]Webhook:[/] {manifest.DiscordWebhookUrl.EscapeMarkup()}");
            return 0;
        }

        if (settings.Url.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            manifest!.DiscordWebhookUrl = null;
            manifest.Save(config!.ActiveGame!.SharedFolderPath);
            AnsiConsole.MarkupLine("[green]Webhook cleared.[/]");
            return 0;
        }

        if (!DiscordWebhook.LooksValid(settings.Url))
        {
            AnsiConsole.MarkupLine(
                "[red]Doesn't look like a Discord webhook URL.[/] " +
                "Expected: https://discord.com/api/webhooks/...");
            return 1;
        }

        manifest!.DiscordWebhookUrl = settings.Url;
        manifest.Save(config!.ActiveGame!.SharedFolderPath);

        // Smoke-test it so the host knows it works before relying on it.
        var ok = DiscordWebhook.PostAsync(
            settings.Url,
            $"**{manifest.GameName}** — civ6-async webhook attached.")
            .GetAwaiter().GetResult();

        AnsiConsole.MarkupLine(ok
            ? "[green]Webhook set and test message posted.[/]"
            : "[yellow]Webhook saved, but test post failed. Double-check the URL.[/]");
        return 0;
    }
}
