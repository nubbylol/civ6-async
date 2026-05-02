using System.Text.Json;
using System.Text.Json.Serialization;

namespace Civ6Async.Cli.Services;

/// <summary>
/// Per-machine helper config: who the local player is, and which shared
/// folder is currently active. Stored at PlatformPaths.AppDataDir()/config.json.
///
/// Version 1 only supports a single active game at a time. Switching games
/// means re-running 'game join' or 'game init' against another shared folder.
/// </summary>
internal sealed class LocalConfig
{
    public string? PlayerName { get; set; }
    public ActiveGameInfo? ActiveGame { get; set; }

    public sealed class ActiveGameInfo
    {
        public required string SharedFolderPath { get; set; }
        public required string GameName        { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ConfigPath =>
        Path.Combine(PlatformPaths.AppDataDir(), "config.json");

    public static LocalConfig Load()
    {
        var path = ConfigPath;
        if (!File.Exists(path)) return new LocalConfig();
        try
        {
            return JsonSerializer.Deserialize<LocalConfig>(File.ReadAllText(path), JsonOptions)
                   ?? new LocalConfig();
        }
        catch
        {
            // Corrupt config — start fresh rather than refuse to run.
            return new LocalConfig();
        }
    }

    public void Save()
    {
        var path = ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicJsonWriter.Write(path, this, JsonOptions);
    }
}
