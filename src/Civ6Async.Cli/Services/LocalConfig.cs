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
        public required string SharedFolderPath { get; set; }
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

    /// <summary>Add or replace a game entry; sets it active.</summary>
    public void RegisterAndActivate(string gameName, string sharedFolderPath)
    {
        Games[gameName] = new GameEntry { SharedFolderPath = sharedFolderPath };
        ActiveGame      = gameName;
    }
}
