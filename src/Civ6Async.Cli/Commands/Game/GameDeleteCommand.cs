using System.ComponentModel;
using Civ6Async.Cli.Services;
using Civ6Async.Cli.Services.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

/// <summary>
/// Permanently destroys a game in cloud storage AND removes the local
/// config entry. Distinct from <see cref="GameLeaveCommand"/>: leave only
/// forgets the game on this machine, the cloud folder keeps existing for
/// other players. Delete wipes everything.
///
/// There is no undo. Other players using the same storage will lose their
/// game too.
/// </summary>
internal sealed class GameDeleteCommand : Command<GameDeleteCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[NAME]")]
        [Description("Game to delete. Picker shown if omitted.")]
        public string? Name { get; init; }

        [CommandOption("-y|--yes")]
        [Description("Skip the confirmation prompt.")]
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
            new SelectionPrompt<string>()
                .Title("Delete which game?")
                .AddChoices(config.Games.Keys));

        if (!config.Games.TryGetValue(name, out var entry))
        {
            AnsiConsole.MarkupLine($"[red]No such game:[/] {name.EscapeMarkup()}");
            return 1;
        }

        IGameStorage storage;
        try
        {
            storage = StorageFactory.From(config, entry);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Couldn't open storage for[/] [bold]{name.EscapeMarkup()}[/]: {ex.Message.EscapeMarkup()}");
            return 1;
        }

        try
        {
            AnsiConsole.MarkupLine($"[bold red]This will permanently delete[/] [bold]{name.EscapeMarkup()}[/]:");
            AnsiConsole.MarkupLine($"  • Cloud storage: [grey]{storage.Description.EscapeMarkup()}[/]");
            AnsiConsole.MarkupLine("  • Manifest, all uploaded saves, history");
            AnsiConsole.MarkupLine("  • This machine's config entry for the game");
            AnsiConsole.MarkupLine("[grey]Other players using the same storage will also lose access. There is no undo.[/]");
            AnsiConsole.WriteLine();

            if (!settings.Yes && !AnsiConsole.Confirm(
                $"Really delete [bold]{name.EscapeMarkup()}[/] from cloud storage?", defaultValue: false))
            {
                AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
                return 1;
            }

            string? error = null;
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start("Wiping cloud storage…", _ =>
                {
                    try { storage.Wipe(); }
                    catch (Exception ex) { error = ex.Message; }
                });

            if (error is not null)
            {
                AnsiConsole.MarkupLine($"[red]Couldn't wipe cloud storage:[/] {error.EscapeMarkup()}");
                AnsiConsole.MarkupLine("[grey]Local config entry was not removed. Try again, or remove the cloud folder manually.[/]");
                return 1;
            }
        }
        finally
        {
            (storage as IDisposable)?.Dispose();
        }

        config.Games.Remove(name);
        if (string.Equals(config.ActiveGame, name, StringComparison.OrdinalIgnoreCase))
            config.ActiveGame = config.Games.Keys.FirstOrDefault();
        config.Save();

        AnsiConsole.MarkupLine($"[green]Deleted[/] [bold]{name.EscapeMarkup()}[/].");
        if (config.ActiveGame is not null)
            AnsiConsole.MarkupLine($"Active game now: [bold]{config.ActiveGame.EscapeMarkup()}[/]");
        return 0;
    }
}
