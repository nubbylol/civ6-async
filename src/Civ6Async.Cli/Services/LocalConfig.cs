using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Civ6Async.Cli.Services;

/// <summary>
/// Per-machine helper config. Schema v4:
///   - playerName            — your identity, shared across all games.
///   - r2 credentials        — account ID, access key, secret key, bucket.
///                             One set per machine, shared across every R2
///                             game (matches the "one Cloudflare account,
///                             many games" reality).
///   - activeGame            — name of the currently-active game (or null).
///   - games                 — map from gameName to per-game state.
///   - defaultR2Prefix       — pre-fill value for the wizard's R2 prefix
///                             prompt; empty means "bucket root".
///   - defaultSharedRoot     — pre-fill value for the local-folder root.
///
/// v3 used Dropbox; v4 swaps to S3/R2 (Dropbox killed long-lived tokens
/// in 2021). On Load, any legacy "dropbox" provider entries are stripped
/// silently — users re-join with R2 credentials.
/// </summary>
internal sealed class LocalConfig
{
    public string?              PlayerName    { get; set; }

    // ---- R2 credentials (per-machine) ----
    public string?              R2AccountId   { get; set; }
    public string?              R2AccessKey   { get; set; }
    public string?              R2SecretKey   { get; set; }
    public string?              R2Bucket      { get; set; }

    public string?              ActiveGame    { get; set; }
    public Dictionary<string, GameEntry> Games { get; set; } = new();

    /// <summary>
    /// Last-used prefix root inside the R2 bucket (parent of per-game
    /// prefixes). Empty / null means "bucket root" — games sit directly
    /// under the bucket as top-level prefixes.
    /// </summary>
    public string? DefaultR2Prefix { get; set; }

    /// <summary>
    /// Last-used local folder root for new joins / inits. Null means never set.
    /// </summary>
    public string? DefaultSharedRoot  { get; set; }

    public sealed class GameEntry
    {
        /// <summary>Local-folder path (Provider == "local").</summary>
        public string? SharedFolderPath { get; set; }

        /// <summary>Storage provider tag — "local" or "r2". Null = legacy local.</summary>
        public string? Provider { get; set; }

        /// <summary>Per-game prefix inside the R2 bucket, e.g. "MyGame" or "season-2/MyGame".</summary>
        public string? R2Prefix { get; set; }
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

            // v3 → v4: drop any leftover "dropbox" provider entries. Their
            // tokens were short-lived anyway and won't work; the user
            // re-joins with R2 credentials.
            var stale = loaded.Games
                .Where(kv => string.Equals(kv.Value.Provider, "dropbox", StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToList();
            foreach (var k in stale) loaded.Games.Remove(k);
            if (stale.Contains(loaded.ActiveGame ?? "", StringComparer.OrdinalIgnoreCase))
                loaded.ActiveGame = loaded.Games.Keys.FirstOrDefault();

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
    /// Add or replace an R2 game entry; sets it active. R2 credentials
    /// (account ID, key, secret, bucket) are set separately at the top
    /// level — per-machine, shared across every R2 game.
    /// </summary>
    public void RegisterAndActivateR2(string gameName, string prefix)
    {
        Games[gameName] = new GameEntry
        {
            Provider = "r2",
            R2Prefix = prefix,
        };
        ActiveGame = gameName;
    }

    /// <summary>True iff every R2 credential field is populated.</summary>
    public bool HasR2Credentials =>
        !string.IsNullOrEmpty(R2AccountId)
        && !string.IsNullOrEmpty(R2AccessKey)
        && !string.IsNullOrEmpty(R2SecretKey)
        && !string.IsNullOrEmpty(R2Bucket);
}
