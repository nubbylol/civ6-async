using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Civ6Async.Cli.Services;

/// <summary>
/// turn_state.json — the source of truth for a shared game. Lives at the
/// root of the shared folder and is read/written by every player's helper.
/// </summary>
internal sealed class GameManifest
{
    public const string FileName = "turn_state.json";
    public const int CurrentSchemaVersion = 1;

    public int          SchemaVersion          { get; set; } = CurrentSchemaVersion;
    public required string GameName             { get; set; }
    public DateTime     CreatedAt              { get; set; }
    public required List<string> Players       { get; set; }
    public required string CurrentPlayer        { get; set; }
    public int          CurrentTurn            { get; set; }
    public string?      LatestSaveFile         { get; set; }
    public string?      LatestSaveHash         { get; set; }
    public DateTime?    LatestSaveSubmittedAt  { get; set; }
    public List<HistoryEntry> History          { get; set; } = new();

    public sealed class HistoryEntry
    {
        public int      Turn       { get; set; }
        public required string Player    { get; set; }
        public required string SavedAs   { get; set; }
        public required string Hash      { get; set; }
        public DateTime At                { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ManifestPathIn(string sharedFolder) =>
        Path.Combine(sharedFolder, FileName);

    public static GameManifest? TryLoad(string sharedFolder)
    {
        var path = ManifestPathIn(sharedFolder);
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<GameManifest>(File.ReadAllText(path), JsonOptions);
    }

    public void Save(string sharedFolder)
    {
        Directory.CreateDirectory(sharedFolder);
        File.WriteAllText(ManifestPathIn(sharedFolder), JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>Compute SHA-256 of a file, formatted as "sha256:<hex>".</summary>
    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        var bytes = SHA256.HashData(stream);
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Advance the manifest to the next player's turn after a successful submit.</summary>
    public void AdvanceTurn(string fromPlayer, int fromTurn, string saveFile, string hash)
    {
        var nextIdx = (Players.IndexOf(fromPlayer) + 1) % Players.Count;
        // Only bump turn counter once we've cycled back to the first player.
        var nextTurn = nextIdx == 0 ? fromTurn + 1 : fromTurn;

        History.Add(new HistoryEntry
        {
            Turn    = fromTurn,
            Player  = fromPlayer,
            SavedAs = saveFile,
            Hash    = hash,
            At      = DateTime.UtcNow,
        });

        CurrentPlayer         = Players[nextIdx];
        CurrentTurn           = nextTurn;
        LatestSaveFile        = saveFile;
        LatestSaveHash        = hash;
        LatestSaveSubmittedAt = DateTime.UtcNow;
    }
}
