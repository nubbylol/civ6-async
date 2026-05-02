using System.Text.Json;

namespace Civ6Async.Cli.Services;

/// <summary>
/// Cooperative lock on the shared folder, preventing two players from
/// submitting simultaneously and racing each other's manifest writes. Lock
/// is a small JSON file (submit.lock) with the holder's identity. If a lock
/// older than StaleAfter is found, callers can take it over (the previous
/// holder probably crashed before releasing).
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

    public static string LockPathIn(string sharedFolder) =>
        Path.Combine(sharedFolder, FileName);

    public static Info? Peek(string sharedFolder) => TryRead(LockPathIn(sharedFolder));

    public static bool TryAcquire(string sharedFolder, string player, out Info? blocking)
    {
        var path = LockPathIn(sharedFolder);
        var existing = TryRead(path);

        if (existing is not null)
        {
            var ours  = string.Equals(existing.Player, player, StringComparison.OrdinalIgnoreCase);
            var stale = DateTime.UtcNow - existing.AcquiredAt > StaleAfter;

            if (!ours && !stale)
            {
                blocking = existing;
                return false;
            }
            // Reaching here means ours, or stale — proceed to overwrite.
        }

        var info = new Info
        {
            Player     = player,
            AcquiredAt = DateTime.UtcNow,
            Hostname   = Environment.MachineName,
        };

        AtomicJsonWriter.Write(path, info, new JsonSerializerOptions { WriteIndented = true });
        blocking = null;
        return true;
    }

    public static void Release(string sharedFolder)
    {
        var path = LockPathIn(sharedFolder);
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static Info? TryRead(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<Info>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }
}
