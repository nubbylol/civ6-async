using Civ6Async.Cli.Services;
using Spectre.Console;

namespace Civ6Async.Cli.Commands.Game;

/// <summary>
/// Loads the local config + active game's manifest. Returns Spectre-markup
/// error strings when anything's missing — callers print them and return.
/// </summary>
internal static class GameContext
{
    public static (LocalConfig? config, GameManifest? manifest, string? err) Resolve()
    {
        var config = LocalConfig.Load();
        if (string.IsNullOrEmpty(config.PlayerName) || config.ActiveGame is null)
        {
            return (null, null,
                "[red]No active game configured.[/] Run [bold]civ6-async game init[/] " +
                "or [bold]civ6-async game join[/] first.");
        }

        var manifest = GameManifest.TryLoad(config.ActiveGame.SharedFolderPath);
        if (manifest is null)
        {
            return (null, null,
                $"[red]Could not read the manifest at[/] " +
                $"[grey]{GameManifest.ManifestPathIn(config.ActiveGame.SharedFolderPath).EscapeMarkup()}[/]. " +
                "Has the shared folder finished syncing?");
        }

        return (config, manifest, null);
    }
}

