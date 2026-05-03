using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Civ6Async.Cli.Services;

/// <summary>
/// Per-machine helper config. Schema v2:
///   - playerName    — your identity, shared across all games on this machine.
///   - activeGame    — name of the currently-active game (null if none).
///   - games         — map from gameName to per-game state (shared folder path).
///
/// Version 1 stored a single object under "activeGame"; Load auto-migrates.
/// </summary>
internal sealed class LocalConfig
{
    public string?              PlayerName  { get; set; }
    public string?              ActiveGame  { get; set; }
    public Dictionary<string, GameEntry> Games { get; set; } = new();

    public sealed class GameEntry
    {
        /// <summary>Local-folder path. Either this or DropboxToken must be set.</summary>
        public string? SharedFolderPath { get; set; }

        /// <summary>Storage provider tag — "local" or "dropbox". Null = legacy local.</summary>
        public string? Provider { get; set; }

        /// <summary>Dropbox access token (Provider == "dropbox").</summary>
        public string? DropboxToken { get; set; }

        /// <summary>Dropbox folder path, e.g. "/civ6-async/MyGame" (Provider == "dropbox").</summary>
        public string? DropboxBasePath { get; set; }
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

            return node.Deserialize<LocalConfig>(JsonOptions) ?? new LocalConfig();
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

    /// <summary>Add or replace a Dropbox game entry; sets it active.</summary>
    public void RegisterAndActivateDropbox(string gameName, string token, string basePath)
    {
        Games[gameName] = new GameEntry
        {
            Provider        = "dropbox",
            DropboxToken    = token,
            DropboxBasePath = basePath,
        };
        ActiveGame = gameName;
    }
}
