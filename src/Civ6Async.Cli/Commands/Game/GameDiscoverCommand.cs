using System.ComponentModel;
using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

/// <summary>
/// Walk a local folder looking for any subdirectory containing a
/// turn_state.json. Useful for local-folder games on a cloud-synced
/// drive when you've been added to a new game and just want to find
/// what's there without being told the exact path. R2 games are
/// discovered via the wizard's join flow, not this command.
/// </summary>
internal sealed class GameDiscoverCommand : Command<GameDiscoverCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--root <PATH>")]
        [Description("Folder to scan. Required.")]
        public required string Root { get; init; }

        [CommandOption("--depth <N>")]
        [Description("Maximum recursion depth. Default 3.")]
        public int Depth { get; init; } = 3;
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(settings.Root))
        {
            AnsiConsole.MarkupLine($"[red]Folder not found:[/] [grey]{settings.Root.EscapeMarkup()}[/]");
            return 1;
        }

        var found = new List<(string Path, GameManifest Manifest)>();
        Walk(settings.Root, settings.Depth, found);

        if (found.Count == 0)
        {
            AnsiConsole.MarkupLine($"[grey]No games found under[/] [grey]{settings.Root.EscapeMarkup()}[/].");
            return 0;
        }

        var table = new Table()
            .AddColumns("Game", "Folder", "Players", "Turn")
            .Border(TableBorder.Rounded);
        foreach (var (path, m) in found)
            table.AddRow(
                m.GameName.EscapeMarkup(),
                path.EscapeMarkup(),
                string.Join(", ", m.Players),
                $"{m.CurrentTurn} ({m.CurrentPlayer})");
        AnsiConsole.Write(table);

        AnsiConsole.MarkupLine(
            "\nTo join one of these, run:\n" +
            "  [bold]civ6-async game join --shared \"<folder>\"[/]");
        return 0;
    }

    private static void Walk(string dir, int depthLeft, List<(string, GameManifest)> found)
    {
        try
        {
            if (File.Exists(Path.Combine(dir, GameManifest.FileName)))
            {
                var m = GameManifest.TryLoad(new Civ6Async.Cli.Services.Storage.LocalFolderStorage(dir));
                if (m is not null) found.Add((dir, m));
            }
            if (depthLeft <= 0) return;
            foreach (var sub in Directory.EnumerateDirectories(dir))
                Walk(sub, depthLeft - 1, found);
        }
        catch
        {
            // Silently skip permission errors etc. Discovery is best-effort.
        }
    }
}
