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
        if (Directory.Exists(installDir))
            Directory.Delete(installDir, recursive: true);
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
