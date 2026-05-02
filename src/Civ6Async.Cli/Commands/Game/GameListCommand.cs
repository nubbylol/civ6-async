using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

internal sealed class GameListCommand : Command<EmptySettings>
{
    protected override int Execute(CommandContext context, EmptySettings settings, CancellationToken cancellationToken)
    {
        var config = LocalConfig.Load();
        if (config.Games.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No games configured. Run[/] [bold]civ6-async game init[/] [grey]or[/] [bold]game join[/][grey].[/]");
            return 0;
        }

        var table = new Table().AddColumns("", "Game", "Shared folder", "Status").Border(TableBorder.Rounded);
        foreach (var (name, entry) in config.Games.OrderBy(kv => kv.Key))
        {
            var active   = name == config.ActiveGame ? "[green]→[/]" : "";
            var manifest = GameManifest.TryLoad(entry.SharedFolderPath);
            var status   = manifest is null
                ? "[red]manifest missing[/]"
                : string.Equals(manifest.CurrentPlayer, config.PlayerName, StringComparison.OrdinalIgnoreCase)
                    ? $"[green]your turn[/] (T{manifest.CurrentTurn})"
                    : $"waiting on [yellow]{manifest.CurrentPlayer}[/] (T{manifest.CurrentTurn})";
            table.AddRow(active, name.EscapeMarkup(), entry.SharedFolderPath.EscapeMarkup(), status);
        }

        AnsiConsole.Write(table);
        if (config.ActiveGame is null)
            AnsiConsole.MarkupLine("\n[yellow]No active game.[/] Use [bold]civ6-async game switch <name>[/] to pick one.");
        return 0;
    }
}
