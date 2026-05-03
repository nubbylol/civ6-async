using System.Runtime.InteropServices;

namespace Civ6Async.Cli.Services;

/// <summary>
/// Locates Civilization VI's user-mods directory across Windows / Linux /
/// macOS, including the Proton path that Linux users running the Windows
/// build via Steam-Play end up with.
/// </summary>
internal static class PlatformPaths
{
    public static IEnumerable<string> CandidateModsDirs()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // SpecialFolder.MyDocuments resolves OneDrive redirection.
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            yield return Path.Combine(docs, "My Games", "Sid Meier's Civilization VI", "Mods");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // Native Aspyr Linux port.
            yield return Path.Combine(home, ".local", "share", "Aspyr", "Sid Meier's Civilization VI", "Mods");

            // Proton (Windows build via Steam Play).
            yield return Path.Combine(home, ".steam", "steam", "steamapps", "compatdata", "289070",
                "pfx", "drive_c", "users", "steamuser", "Documents",
                "My Games", "Sid Meier's Civilization VI", "Mods");

            // Flatpak Steam Proton.
            yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data",
                "Steam", "steamapps", "compatdata", "289070",
                "pfx", "drive_c", "users", "steamuser", "Documents",
                "My Games", "Sid Meier's Civilization VI", "Mods");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(home, "Library", "Application Support",
                "Sid Meier's Civilization VI", "Mods");
        }
    }

    /// <summary>
    /// Returns the first candidate Mods directory whose parent (the Civ user
    /// folder) actually exists, or null if none do.
    /// </summary>
    public static string? AutoDetectModsDir()
    {
        foreach (var dir in CandidateModsDirs())
        {
            var civUserDir = Path.GetDirectoryName(dir);
            if (civUserDir is not null && Directory.Exists(civUserDir))
                return dir;
        }
        return null;
    }

    /// <summary>
    /// Hotseat saves live alongside the Mods directory under the Civ user
    /// folder. Same per-OS heuristics, just one folder over.
    /// </summary>
    public static IEnumerable<string> CandidateHotseatSavesDirs() =>
        CandidateModsDirs().Select(modsDir =>
            Path.Combine(Path.GetDirectoryName(modsDir)!, "Saves", "Hotseat"));

    public static string? AutoDetectHotseatSavesDir()
    {
        foreach (var dir in CandidateHotseatSavesDirs())
        {
            var civUserDir = Path.GetDirectoryName(Path.GetDirectoryName(dir));
            if (civUserDir is not null && Directory.Exists(civUserDir))
                return dir;
        }
        return null;
    }

    /// <summary>
    /// Civ 6's Lua.log lives under the per-user "Logs" folder (sibling of
    /// Mods / Saves). Same per-OS detection as the Mods folder; we just
    /// step out of "Mods" and into "Logs/Lua.log".
    /// </summary>
    public static string? AutoDetectLuaLogPath()
    {
        var modsDir = AutoDetectModsDir();
        if (modsDir is null) return null;
        var civUser = Path.GetDirectoryName(modsDir);
        if (civUser is null) return null;
        // On Windows the Logs/ folder lives in %LOCALAPPDATA%\Firaxis Games\...,
        // not in My Games\... where the user-installed Mods do. Try both.
        var candidates = new[]
        {
            Path.Combine(civUser, "Logs", "Lua.log"),
            // %LOCALAPPDATA% override on Windows.
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Firaxis Games", "Sid Meier's Civilization VI", "Logs", "Lua.log")
                : null,
        };
        foreach (var c in candidates)
            if (c is not null && File.Exists(c)) return c;
        return null;
    }

    /// <summary>
    /// Where civ6-async writes its own state (config.json). Lives directly
    /// next to the running executable so the helper is fully portable —
    /// copy the binary anywhere (USB stick, sync-protected folder, custom
    /// location) and its state travels with it. Falls back to the current
    /// working directory if the executable path is somehow unavailable
    /// (e.g. running via 'dotnet run' from source).
    /// </summary>
    public static string AppDataDir()
    {
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            var dir = Path.GetDirectoryName(exePath);
            if (!string.IsNullOrEmpty(dir)) return dir;
        }
        return Environment.CurrentDirectory;
    }
}
