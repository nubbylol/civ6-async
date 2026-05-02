using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

/// <summary>
/// Prints a one-line "join command" suitable for pasting into Discord — so
/// the host doesn't have to explain shared paths and game names. Joiners
/// just paste-and-run.
/// </summary>
internal sealed class GameInviteCommand : Command<EmptySettings>
{
    protected override int Execute(CommandContext context, EmptySettings settings, CancellationToken cancellationToken)
    {
        var (config, manifest, err) = GameContext.Resolve();
        if (err is not null) { AnsiConsole.MarkupLine(err); return 1; }

        var path = config!.ActiveGameEntry!.SharedFolderPath;

        AnsiConsole.MarkupLine($"[grey]Send this command to invitees for[/] [bold]{manifest!.GameName.EscapeMarkup()}[/][grey]:[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [bold]civ6-async game join --shared \"{path.EscapeMarkup()}\"[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            "[grey]Note: each invitee needs that exact path on their own machine. " +
            "If you're using Dropbox/Drive/etc., the cloud-sync client will create " +
            "the folder for them; the path is usually the same modulo their username.[/]");
        return 0;
    }
}
