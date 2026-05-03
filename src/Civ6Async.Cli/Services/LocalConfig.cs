using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Civ6Async.Cli.Services;

/// <summary>
/// Per-machine helper config. Schema v3:
///   - playerName            — your identity, shared across all games.
///   - dropboxToken          — your Dropbox access token, shared across
///                             all Dropbox games (one per machine).
///   - activeGame            — name of the currently-active game (or null).
///   - games                 — map from gameName to per-game state.
///   - defaultDropboxRoot /
///     defaultSharedRoot     — pre-fill values for the wizard's storage
///                             prompts.
///
/// Version 1 stored a single object under "activeGame"; v2 added Games
/// with per-game DropboxToken. Both auto-migrate to v3 on Load.
/// </summary>
internal sealed class LocalConfig
{
    public string?              PlayerName    { get; set; }
    public string?              DropboxToken  { get; set; }
    public string?              ActiveGame    { get; set; }
    public Dictionary<string, GameEntry> Games { get; set; } = new();

    /// <summary>
    /// Last-used Dropbox root folder for new joins / inits. Empty string means
    /// "App folder root". Null means never set — wizard prompts will offer
    /// empty as the default.
    /// </summary>
    public string? DefaultDropboxRoot { get; set; }

    /// <summary>
    /// Last-used local folder root for new joins / inits. Null means never set.
    /// </summary>
    public string? DefaultSharedRoot  { get; set; }

    public sealed class GameEntry
    {
        /// <summary>Local-folder path (Provider == "local").</summary>
        public string? SharedFolderPath { get; set; }

        /// <summary>Storage provider tag — "local" or "dropbox". Null = legacy local.</summary>
        public string? Provider { get; set; }

        /// <summary>Dropbox folder path, e.g. "/civ6-async/MyGame" (Provider == "dropbox").</summary>
        public string? DropboxBasePath { get; set; }

        /// <summary>
        /// Legacy v2 field. Tokens now live at the top of LocalConfig.
        /// Kept here so old config.json files still deserialize; cleared on
        /// migration in Load().
        /// </summary>
        public string? DropboxToken { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ConfigPath =>
        Path.Combine(PlatformPaths.AppDataDir(), "config.json");

    public GameEntry? ActiveGameEntry =>
        ActiveGame is not null && Games.TryGetValue(ActiveGame, out var e) ? e : null;


    public static LocalConfig Load()
    {
        var path = ConfigPath;
        if (!File.Exists(path)) return new LocalConfig();

        try
        {
            var text = File.ReadAllText(path);
            var node = JsonNode.Parse(text);
            if (node is null) return new LocalConfig();

            // Detect v1 (activeGame as object) and migrate.
            if (node["activeGame"] is JsonObject oldActive)
            {
                var oldName = oldActive["GameName"]?.GetValue<string>()
                              ?? oldActive["gameName"]?.GetValue<string>();
                var oldPath = oldActive["SharedFolderPath"]?.GetValue<string>()
                              ?? oldActive["sharedFolderPath"]?.GetValue<string>();
                node["activeGame"] = oldName;
                if (oldName is not null && oldPath is not null)
                {
                    var games = node["games"]?.AsObject() ?? new JsonObject();
                    games[oldName] = new JsonObject
                    {
                        ["sharedFolderPath"] = oldPath,
                    };
                    node["games"] = games;
                }
            }

            var loaded = node.Deserialize<LocalConfig>(JsonOptions) ?? new LocalConfig();

            // v2 → v3 migration: lift any per-game DropboxToken to the
            // top-level field. Tokens are per-machine, not per-game; this
            // matches what every real user already had (one Dropbox app,
            // one token, used across every game).
            if (string.IsNullOrEmpty(loaded.DropboxToken))
            {
                foreach (var entry in loaded.Games.Values)
                {
                    if (entry.Provider == "dropbox" && !string.IsNullOrEmpty(entry.DropboxToken))
                    {
                        loaded.DropboxToken = entry.DropboxToken;
                        break;
                    }
                }
            }
            foreach (var entry in loaded.Games.Values)
                entry.DropboxToken = null;

            return loaded;
        }
        catch
        {
            return new LocalConfig();
        }
    }

    public void Save()
    {
        var path = ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicJsonWriter.Write(path, this, JsonOptions);
    }

    /// <summary>Add or replace a local-folder game entry; sets it active.</summary>
    public void RegisterAndActivate(string gameName, string sharedFolderPath)
    {
        Games[gameName] = new GameEntry
        {
            Provider         = "local",
            SharedFolderPath = sharedFolderPath,
        };
        ActiveGame = gameName;
    }

    /// <summary>
    /// Add or replace a Dropbox game entry; sets it active. The token
    /// itself is set separately at the top level of LocalConfig, since
    /// it's a per-machine value shared across every Dropbox game.
    /// </summary>
    public void RegisterAndActivateDropbox(string gameName, string basePath)
    {
        Games[gameName] = new GameEntry
        {
            Provider        = "dropbox",
            DropboxBasePath = basePath,
        };
        ActiveGame = gameName;
    }
}
