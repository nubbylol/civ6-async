using System.Text.Json;
using Civ6Async.Cli.Services.Storage;

namespace Civ6Async.Cli.Services;

/// <summary>
/// Cooperative lock on the shared storage, preventing two players from
/// submitting simultaneously. JSON file (submit.lock) at the storage root.
/// Stale locks (older than StaleAfter) are taken over silently.
/// </summary>
internal static class SubmitLock
{
    public const  string   FileName    = "submit.lock";
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    public sealed class Info
    {
        public required string   Player    { get; set; }
        public DateTime AcquiredAt { get; set; }
        public required string   Hostname  { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static Info? Peek(IGameStorage storage) => TryRead(storage);

    public static bool TryAcquire(IGameStorage storage, string player, out Info? blocking)
    {
        var existing = TryRead(storage);

        if (existing is not null)
        {
            var ours  = string.Equals(existing.Player, player, StringComparison.OrdinalIgnoreCase);
            var stale = DateTime.UtcNow - existing.AcquiredAt > StaleAfter;
            if (!ours && !stale)
            {
                blocking = existing;
                return false;
            }
        }

        var info = new Info
        {
            Player     = player,
            AcquiredAt = DateTime.UtcNow,
            Hostname   = Environment.MachineName,
        };
        storage.WriteBytes(FileName, JsonSerializer.SerializeToUtf8Bytes(info, JsonOpts));
        blocking = null;
        return true;
    }

    public static void Release(IGameStorage storage)
    {
        try { storage.Delete(FileName); } catch { }
    }

    private static Info? TryRead(IGameStorage storage)
    {
        if (!storage.Exists(FileName)) return null;
        try
        {
            return JsonSerializer.Deserialize<Info>(storage.ReadBytes(FileName));
        }
        catch
        {
            return null;
        }
    }
}
