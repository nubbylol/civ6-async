using Civ6Async.Cli.Services;
using Civ6Async.Cli.Services.Storage;
using Spectre.Console;

namespace Civ6Async.Cli.Commands.Game;

internal static class GameContext
{
    public sealed record Resolved(LocalConfig Config, IGameStorage Storage, GameManifest Manifest);

    public static (Resolved? ctx, string? err) Resolve()
    {
        var config = LocalConfig.Load();
        if (string.IsNullOrEmpty(config.PlayerName))
        {
            return (null,
                "[red]No player identity configured.[/] Run [bold]civ6-async game init[/] " +
                "or [bold]civ6-async game join[/] first.");
        }

        var entry = config.ActiveGameEntry;
        if (entry is null)
        {
            return (null,
                "[red]No active game.[/] " +
                (config.Games.Count == 0
                    ? "Run [bold]civ6-async game init[/] or [bold]civ6-async game join[/]."
                    : $"You have games available: {string.Join(", ", config.Games.Keys)}. " +
                      "Use [bold]civ6-async game switch <name>[/]."));
        }

        IGameStorage storage;
        try
        {
            storage = StorageFactory.From(entry);
        }
        catch (Exception ex)
        {
            return (null, $"[red]Couldn't open game storage:[/] {ex.Message.EscapeMarkup()}");
        }

        var manifest = GameManifest.TryLoad(storage);
        if (manifest is null)
        {
            return (null,
                $"[red]Could not read the manifest from[/] [grey]{storage.Description.EscapeMarkup()}[/]. " +
                "Has it been created / synced yet?");
        }

        return (new Resolved(config, storage, manifest), null);
    }
}
