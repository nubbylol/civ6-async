using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Civ6Async.Cli.Services.Storage;

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
    /// <summary>Optional Discord webhook URL for "your turn" pings on submit.</summary>
    public string?      DiscordWebhookUrl      { get; set; }
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

    public static GameManifest? TryLoad(IGameStorage storage)
    {
        if (!storage.Exists(FileName)) return null;
        try
        {
            return JsonSerializer.Deserialize<GameManifest>(storage.ReadBytes(FileName), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Save(IGameStorage storage)
    {
        storage.WriteBytes(FileName, JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions));
    }

    /// <summary>Compute SHA-256 of a file, formatted as "sha256:<hex>".</summary>
    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        var bytes = SHA256.HashData(stream);
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Record a submitted save and set the next state. Caller supplies the
    /// authoritative <paramref name="nextTurn"/> and <paramref name="nextPlayer"/>
    /// — usually pulled from a Lua-log save_complete event so we capture the
    /// real game state after the in-game mod cycled through any number of
    /// auto-ended turns.
    /// </summary>
    public void RecordSubmit(
        string submittingPlayer,
        int    submittedAtTurn,
        string saveFile,
        string hash,
        int    nextTurn,
        string nextPlayer)
    {
        History.Add(new HistoryEntry
        {
            Turn    = submittedAtTurn,
            Player  = submittingPlayer,
            SavedAs = saveFile,
            Hash    = hash,
            At      = DateTime.UtcNow,
        });

        CurrentPlayer         = nextPlayer;
        CurrentTurn           = nextTurn;
        LatestSaveFile        = saveFile;
        LatestSaveHash        = hash;
        LatestSaveSubmittedAt = DateTime.UtcNow;
    }
}
