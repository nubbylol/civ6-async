using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

internal sealed class GameStatusCommand : Command<EmptySettings>
{
    protected override int Execute(CommandContext context, EmptySettings settings, CancellationToken cancellationToken)
    {
        var (config, manifest, err) = GameContext.Resolve();
        if (err is not null) { AnsiConsole.MarkupLine(err); return 1; }

        var iAmUp = string.Equals(manifest!.CurrentPlayer, config!.PlayerName,
            StringComparison.OrdinalIgnoreCase);

        var grid = new Grid().AddColumn().AddColumn();
        grid.AddRow("Game",        $"[bold]{manifest.GameName.EscapeMarkup()}[/]");
        grid.AddRow("Shared",      $"[grey]{config.ActiveGame!.SharedFolderPath.EscapeMarkup()}[/]");
        grid.AddRow("Turn",        manifest.CurrentTurn.ToString());
        grid.AddRow("Whose turn",  iAmUp
            ? $"[green]{manifest.CurrentPlayer}[/] (you)"
            : $"[yellow]{manifest.CurrentPlayer}[/]");
        grid.AddRow("Players",     string.Join(" → ", manifest.Players));
        if (manifest.LatestSaveSubmittedAt is not null)
            grid.AddRow("Last submit", $"[grey]{manifest.LatestSaveSubmittedAt.Value:yyyy-MM-dd HH:mm} UTC[/]");

        AnsiConsole.Write(grid);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(iAmUp
            ? "It's [green]your turn[/]. Run [bold]civ6-async game check[/] to download the latest save."
            : $"Waiting on [yellow]{manifest.CurrentPlayer}[/].");
        return 0;
    }
}

internal sealed class EmptySettings : CommandSettings { }
