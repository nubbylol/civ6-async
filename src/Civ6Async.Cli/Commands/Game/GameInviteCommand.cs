using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

/// <summary>
/// Prints a copy-pasteable join command for the active game. Format depends
/// on the storage provider — for local-folder games, callers will need to
/// translate the host's path to their own local path; for Dropbox games,
/// the join command embeds the access token and works as-is on every
/// player's machine.
/// </summary>
internal sealed class GameInviteCommand : Command<EmptySettings>
{
    protected override int Execute(CommandContext context, EmptySettings settings, CancellationToken cancellationToken)
    {
        var (ctx, err) = GameContext.Resolve();
        if (err is not null) { AnsiConsole.MarkupLine(err); return 1; }
        var (config, manifest) = (ctx!.Config, ctx.Manifest);
        var entry = config.ActiveGameEntry!;

        AnsiConsole.MarkupLine($"[grey]Send this to invitees for[/] [bold]{manifest.GameName.EscapeMarkup()}[/][grey]:[/]");
        AnsiConsole.WriteLine();

        if (entry.Provider == "dropbox")
        {
            AnsiConsole.MarkupLine(
                $"  [bold]civ6-async game join --dropbox-token \"{entry.DropboxToken}\" " +
                $"--dropbox-folder \"{entry.DropboxBasePath}\"[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(
                "[grey]The token gives the holder read+write access to that Dropbox folder. " +
                "Share it only with people you'd give save-file access to.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine(
                $"  [bold]civ6-async game join --shared \"{entry.SharedFolderPath.EscapeMarkup()}\"[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(
                "[grey]Note: each invitee needs that exact path on their own machine. " +
                "If you're using Dropbox/Drive/etc., the cloud-sync client will create " +
                "the folder for them; the path is usually the same modulo their username.[/]");
        }
        return 0;
    }
}
