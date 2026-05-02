using System.Security.Cryptography;

namespace Civ6Async.Cli.Services;

internal static class ModInstaller
{
    public static string GetInstallDir(string modsDir) =>
        Path.Combine(modsDir, EmbeddedMod.ModFolderName);

    /// <summary>
    /// Writes every embedded mod file into <paramref name="modsDir"/>/civ6-async/.
    /// Existing files are overwritten (this doubles as "update").
    /// </summary>
    public static void Install(string modsDir)
    {
        var installDir = GetInstallDir(modsDir);
        Directory.CreateDirectory(installDir);

        foreach (var file in EmbeddedMod.Files)
        {
            var dest = Path.Combine(installDir, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllBytes(dest, EmbeddedMod.Read(file.ResourceName));
        }
    }

    public static bool IsInstalled(string modsDir) =>
        Directory.Exists(GetInstallDir(modsDir));

    public static void Uninstall(string modsDir)
    {
        var installDir = GetInstallDir(modsDir);
        if (!Directory.Exists(installDir)) return;
        ForceDeleteDirectory(installDir);
    }

    /// <summary>
    /// Robust recursive delete. OneDrive-redirected Documents paths break
    /// naive Directory.Delete in two ways: synced files get a ReadOnly
    /// attribute (which Delete refuses), and Files-On-Demand entries can
    /// hold transient locks during sync. We:
    ///   1. Clear ReadOnly on the root, every subdirectory, every file.
    ///   2. Try Directory.Delete with a short retry/backoff.
    ///   3. Fall back to a manual depth-first per-file delete.
    /// </summary>
    private static void ForceDeleteDirectory(string path)
    {
        ClearReadOnlyRecursive(path);

        Exception? lastEx = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastEx = ex;
                Thread.Sleep(150 * (attempt + 1));
                ClearReadOnlyRecursive(path);
            }
        }

        // Manual fallback: walk depth-first, deleting per-file.
        try
        {
            DeleteRecursiveManual(new DirectoryInfo(path));
            return;
        }
        catch (Exception ex)
        {
            lastEx = ex;
        }

        if (lastEx is not null) throw lastEx;
    }

    private static void ClearReadOnlyRecursive(string path)
    {
        if (!Directory.Exists(path)) return;

        TryClearReadOnly(path);

        foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
            TryClearReadOnly(entry);
    }

    private static void TryClearReadOnly(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
        }
        catch
        {
            // Best-effort; the delete will surface a real error if we can't
            // get past whatever's blocking us.
        }
    }

    private static void DeleteRecursiveManual(DirectoryInfo dir)
    {
        foreach (var file in dir.GetFiles())
        {
            file.Attributes = FileAttributes.Normal;
            file.Delete();
        }
        foreach (var sub in dir.GetDirectories())
            DeleteRecursiveManual(sub);

        dir.Attributes = FileAttributes.Normal;
        dir.Delete(recursive: false);
    }

    /// <summary>
    /// Returns null if every installed file matches its embedded counterpart
    /// byte-for-byte; otherwise returns a list of mismatched/missing paths.
    /// </summary>
    public static List<string>? VerifyIntegrity(string modsDir)
    {
        var installDir = GetInstallDir(modsDir);
        if (!Directory.Exists(installDir))
            return EmbeddedMod.Files.Select(f => f.RelativePath).ToList();

        var diffs = new List<string>();
        foreach (var file in EmbeddedMod.Files)
        {
            var dest = Path.Combine(installDir, file.RelativePath);
            if (!File.Exists(dest))
            {
                diffs.Add(file.RelativePath + " (missing)");
                continue;
            }

            var expected = SHA256.HashData(EmbeddedMod.Read(file.ResourceName));
            var actual   = SHA256.HashData(File.ReadAllBytes(dest));
            if (!expected.AsSpan().SequenceEqual(actual))
                diffs.Add(file.RelativePath + " (modified)");
        }
        return diffs.Count == 0 ? null : diffs;
    }
}
