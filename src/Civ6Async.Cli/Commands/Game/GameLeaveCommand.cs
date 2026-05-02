using System.ComponentModel;
using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

/// <summary>
/// Removes a game from the local config. Doesn't touch the shared folder —
/// other players can still keep playing it; you've just stopped tracking it.
/// </summary>
internal sealed class GameLeaveCommand : Command<GameLeaveCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[NAME]")]
        [Description("Game to forget. If omitted, an interactive picker is shown.")]
        public string? Name { get; init; }

        [CommandOption("-y|--yes")]
        public bool Yes { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var config = LocalConfig.Load();
        if (config.Games.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No games configured.[/]");
            return 0;
        }

        var name = settings.Name ?? AnsiConsole.Prompt(
            new SelectionPrompt<string>().Title("Forget which game?").AddChoices(config.Games.Keys));

        if (!config.Games.ContainsKey(name))
        {
            AnsiConsole.MarkupLine($"[red]No such game:[/] {name.EscapeMarkup()}");
            return 1;
        }

        if (!settings.Yes && !AnsiConsole.Confirm(
            $"Forget [bold]{name.EscapeMarkup()}[/]? This only removes it from your local config; " +
            $"the shared folder is untouched."))
        {
            AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
            return 1;
        }

        config.Games.Remove(name);
        if (config.ActiveGame == name)
            config.ActiveGame = config.Games.Keys.FirstOrDefault();
        config.Save();

        AnsiConsole.MarkupLine($"[green]Forgot[/] [bold]{name.EscapeMarkup()}[/].");
        if (config.ActiveGame is not null)
            AnsiConsole.MarkupLine($"Active game now: [bold]{config.ActiveGame.EscapeMarkup()}[/]");
        return 0;
    }
}
