namespace Civ6Async.Cli.Services;

/// <summary>
/// Parses Civ 6's Lua.log for events emitted by our in-game EventLogger.
/// Format every entry has: "...civ6-async|<event>|key=value|key=value|..."
/// (Civ's print prefixes its own context name; we ignore everything before
/// the literal "civ6-async|" marker.)
/// </summary>
internal static class LuaLogReader
{
    public const string EventPrefix = "civ6-async|";

    public sealed class Event
    {
        public required string                       Name   { get; init; }
        public required IReadOnlyDictionary<string, string> Fields { get; init; }
        public          long                          LineOffset { get; init; }

        public int?    Turn       => TryGetInt(Fields, "turn");
        public string? Player     => Fields.GetValueOrDefault("player");
        public int?    PlayerId   => TryGetInt(Fields, "playerId");
        public bool    IsHotseat  => Fields.GetValueOrDefault("isHotseat") == "true";

        private static int? TryGetInt(IReadOnlyDictionary<string, string> f, string key) =>
            f.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : null;
    }

    /// <summary>
    /// Read every civ6-async event currently in the log file. Newest last.
    /// Returns an empty list if the log doesn't exist or can't be opened.
    /// </summary>
    public static IReadOnlyList<Event> ReadAll(string? logPath = null)
    {
        logPath ??= PlatformPaths.AutoDetectLuaLogPath();
        if (logPath is null || !File.Exists(logPath)) return Array.Empty<Event>();

        var events = new List<Event>();
        try
        {
            // FileShare.ReadWrite — Civ keeps the file open with a write lock,
            // so we have to allow concurrent writers or we can't read it at all.
            using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            string? line;
            long pos  = 0;
            while ((line = sr.ReadLine()) is not null)
            {
                var idx = line.IndexOf(EventPrefix, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    var parsed = Parse(line[(idx + EventPrefix.Length)..]);
                    if (parsed is not null)
                        events.Add(new Event { Name = parsed.Value.name, Fields = parsed.Value.fields, LineOffset = pos });
                }
                pos += (line.Length + Environment.NewLine.Length);
            }
        }
        catch
        {
            // Best-effort; fall through with whatever we collected.
        }
        return events;
    }

    /// <summary>
    /// The most recent event whose name matches one of <paramref name="names"/>.
    /// </summary>
    public static Event? FindLatest(IEnumerable<string> names, string? logPath = null)
    {
        var nameSet = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var all     = ReadAll(logPath);
        for (var i = all.Count - 1; i >= 0; i--)
            if (nameSet.Contains(all[i].Name)) return all[i];
        return null;
    }

    private static (string name, IReadOnlyDictionary<string, string> fields)? Parse(string body)
    {
        var parts = body.Split('|');
        if (parts.Length == 0) return null;
        var name   = parts[0].Trim();
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < parts.Length; i++)
        {
            var eq = parts[i].IndexOf('=');
            if (eq <= 0) continue;
            fields[parts[i][..eq].Trim()] = parts[i][(eq + 1)..].Trim();
        }
        return (name, fields);
    }
}
