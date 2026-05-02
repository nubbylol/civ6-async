using Civ6Async.Cli.Services;
using Spectre.Console;

namespace Civ6Async.Cli.Commands.Game;

/// <summary>
/// Loads the local config + the active game's manifest. Returns
/// Spectre-markup error strings when anything's missing — callers print
/// them and return.
/// </summary>
internal static class GameContext
{
    public static (LocalConfig? config, GameManifest? manifest, string? err) Resolve()
    {
        var config = LocalConfig.Load();
        if (string.IsNullOrEmpty(config.PlayerName))
        {
            return (null, null,
                "[red]No player identity configured.[/] Run [bold]civ6-async game init[/] " +
                "or [bold]civ6-async game join[/] first.");
        }

        var entry = config.ActiveGameEntry;
        if (entry is null)
        {
            return (null, null,
                "[red]No active game.[/] " +
                (config.Games.Count == 0
                    ? "Run [bold]civ6-async game init[/] or [bold]civ6-async game join[/]."
                    : $"You have games available: {string.Join(", ", config.Games.Keys)}. " +
                      "Use [bold]civ6-async game switch <name>[/]."));
        }

        var manifest = GameManifest.TryLoad(entry.SharedFolderPath);
        if (manifest is null)
        {
            return (null, null,
                $"[red]Could not read the manifest at[/] " +
                $"[grey]{GameManifest.ManifestPathIn(entry.SharedFolderPath).EscapeMarkup()}[/]. " +
                "Has the shared folder finished syncing?");
        }

        return (config, manifest, null);
    }
}
