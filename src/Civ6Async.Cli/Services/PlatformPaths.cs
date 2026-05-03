using System.Runtime.InteropServices;

namespace Civ6Async.Cli.Services;

/// <summary>
/// Locates Civilization VI's per-user data across Windows / Linux / macOS,
/// including Steam-Play / Proton variants for Linux users running the
/// Windows build.
///
/// Civ uses TWO user folders per profile on Windows:
///   - "My Games" — user-installed mods, save files
///   - "AppData/Local" — engine logs, caches, Mods.sqlite (mod database)
///
/// Linux Aspyr collapses both into a single ~/.local/share/Aspyr/... folder.
/// Proton replicates the Windows two-folder split *inside* the Proton prefix
/// (drive_c/users/steamuser/Documents and drive_c/users/steamuser/AppData).
/// </summary>
internal static class PlatformPaths
{
    /// <summary>
    /// One candidate Civ profile = a pair of (myGames root, appDataLocal root).
    /// Either path may be the same on platforms that don't split.
    /// </summary>
    public sealed record CivProfile(string MyGamesRoot, string AppDataLocalRoot);

    public static IEnumerable<CivProfile> Candidates()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var docs  = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return new CivProfile(
                Path.Combine(docs, "My Games", "Sid Meier's Civilization VI"),
                Path.Combine(local, "Firaxis Games", "Sid Meier's Civilization VI"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // Native Aspyr port — one folder for everything.
            var aspyr = Path.Combine(home, ".local", "share", "Aspyr", "Sid Meier's Civilization VI");
            yield return new CivProfile(aspyr, aspyr);

            // Proton variants — Windows-style split inside the prefix.
            foreach (var prefix in ProtonPrefixCandidates(home))
            {
                var users = Path.Combine(prefix, "drive_c", "users", "steamuser");
                yield return new CivProfile(
                    Path.Combine(users, "Documents", "My Games", "Sid Meier's Civilization VI"),
                    Path.Combine(users, "AppData",   "Local",    "Firaxis Games", "Sid Meier's Civilization VI"));
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var lib  = Path.Combine(home, "Library", "Application Support", "Sid Meier's Civilization VI");
            yield return new CivProfile(lib, lib);
        }
    }

    private static IEnumerable<string> ProtonPrefixCandidates(string home)
    {
        // Standard Steam install (the Deck default).
        yield return Path.Combine(home, ".steam", "steam", "steamapps", "compatdata", "289070", "pfx");
        // Older Steam layout / some distros.
        yield return Path.Combine(home, ".local", "share", "Steam", "steamapps", "compatdata", "289070", "pfx");
        // Even older.
        yield return Path.Combine(home, ".steam", "root", "steamapps", "compatdata", "289070", "pfx");
        // Flatpak Steam.
        yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data",
            "Steam", "steamapps", "compatdata", "289070", "pfx");
    }

    private static CivProfile? FirstExisting()
    {
        foreach (var p in Candidates())
        {
            if (Directory.Exists(p.MyGamesRoot)) return p;
            if (Directory.Exists(p.AppDataLocalRoot)) return p;
        }
        return null;
    }

    // ---- Public detector helpers ----

    public static string? AutoDetectModsDir() =>
        FirstExisting() is { } p ? Path.Combine(p.MyGamesRoot, "Mods") : null;

    public static string? AutoDetectHotseatSavesDir() =>
        FirstExisting() is { } p ? Path.Combine(p.MyGamesRoot, "Saves", "Hotseat") : null;

    public static string? AutoDetectLuaLogPath()
    {
        var p = FirstExisting();
        if (p is null) return null;
        var path = Path.Combine(p.AppDataLocalRoot, "Logs", "Lua.log");
        return File.Exists(path) ? path : null;
    }

    public static string? AutoDetectModsDbPath()
    {
        var p = FirstExisting();
        if (p is null) return null;
        var path = Path.Combine(p.AppDataLocalRoot, "Mods.sqlite");
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Where civ6-async writes its own state (config.json). Lives directly
    /// next to the running executable so the helper is fully portable.
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
