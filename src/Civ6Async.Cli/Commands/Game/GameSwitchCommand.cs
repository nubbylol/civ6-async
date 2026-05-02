using System.ComponentModel;
using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

internal sealed class GameSwitchCommand : Command<GameSwitchCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[NAME]")]
        [Description("Game to switch to. If omitted, an interactive picker is shown.")]
        public string? Name { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var config = LocalConfig.Load();
        if (config.Games.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No games configured.[/]");
            return 1;
        }

        var name = settings.Name ?? AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Switch to which game?")
                .AddChoices(config.Games.Keys));

        if (!config.Games.ContainsKey(name))
        {
            AnsiConsole.MarkupLine($"[red]No such game:[/] [grey]{name.EscapeMarkup()}[/]. " +
                $"Known: {string.Join(", ", config.Games.Keys)}");
            return 1;
        }

        config.ActiveGame = name;
        config.Save();
        AnsiConsole.MarkupLine($"[green]Active game:[/] [bold]{name.EscapeMarkup()}[/]");
        return 0;
    }
}
