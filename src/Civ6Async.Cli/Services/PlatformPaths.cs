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
}
