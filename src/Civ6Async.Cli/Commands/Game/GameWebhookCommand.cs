using System.ComponentModel;
using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

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
        var (ctx, err) = GameContext.Resolve();
        if (err is not null) { AnsiConsole.MarkupLine(err); return 1; }
        var (storage, manifest) = (ctx!.Storage, ctx.Manifest);

        var url = settings.Url;
        if (url is null)
        {
            AnsiConsole.MarkupLine(
                manifest.DiscordWebhookUrl is null
                    ? "[grey]No webhook configured.[/]"
                    : $"[grey]Current webhook:[/] {manifest.DiscordWebhookUrl.EscapeMarkup()}");

            var action = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("What do you want to do?")
                    .AddChoices(
                        "Set a new webhook URL",
                        manifest.DiscordWebhookUrl is null ? "Cancel" : "Clear the current webhook",
                        "Cancel"));

            if (action.StartsWith("Cancel", StringComparison.OrdinalIgnoreCase)) return 0;
            if (action.StartsWith("Clear",  StringComparison.OrdinalIgnoreCase)) url = "clear";
            else
            {
                url = AnsiConsole.Prompt(
                    new TextPrompt<string>("Discord webhook URL:")
                        .Validate(s => DiscordWebhook.LooksValid(s)
                            ? ValidationResult.Success()
                            : ValidationResult.Error("[red]Doesn't look like a Discord webhook URL.[/]")));
            }
        }

        if (url.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            manifest.DiscordWebhookUrl = null;
            manifest.Save(storage);
            AnsiConsole.MarkupLine("[green]Webhook cleared.[/]");
            return 0;
        }

        if (!DiscordWebhook.LooksValid(url))
        {
            AnsiConsole.MarkupLine(
                "[red]Doesn't look like a Discord webhook URL.[/] " +
                "Expected: https://discord.com/api/webhooks/...");
            return 1;
        }

        manifest.DiscordWebhookUrl = url;
        manifest.Save(storage);

        var ok = DiscordWebhook.PostAsync(
            url,
            $"**{manifest.GameName}** — civ6-async webhook attached.")
            .GetAwaiter().GetResult();

        AnsiConsole.MarkupLine(ok
            ? "[green]Webhook set and test message posted.[/]"
            : "[yellow]Webhook saved, but test post failed. Double-check the URL.[/]");
        return 0;
    }
}
