using Spectre.Console;

namespace Civ6Async.Cli.Services;

internal static class SaveDownloader
{
    public enum Status
    {
        /// <summary>Manifest hasn't published a save yet (turn 1, fresh game).</summary>
        NoSaveYet,
        /// <summary>Local file already matches the shared one — nothing to do.</summary>
        AlreadyHave,
        /// <summary>Save in shared folder differs from local; we ought to copy.</summary>
        Stale,
        /// <summary>Civ saves folder couldn't be found.</summary>
        SavesDirMissing,
        /// <summary>Manifest references a file that hasn't synced yet.</summary>
        SourceMissing,
    }

    public sealed record Plan(
        Status Status,
        string? SourcePath,
        string? DestPath,
        string? DestName);

    /// <summary>
    /// Inspect what needs to happen to bring the local Civ saves folder in
    /// sync with the manifest's latest save. Doesn't actually copy anything.
    /// </summary>
    public static Plan Inspect(LocalConfig config, GameManifest manifest)
    {
        if (manifest.LatestSaveFile is null)
            return new Plan(Status.NoSaveYet, null, null, null);

        var savesDir = PlatformPaths.AutoDetectHotseatSavesDir();
        if (savesDir is null)
            return new Plan(Status.SavesDirMissing, null, null, null);

        var src = Path.Combine(config.ActiveGameEntry!.SharedFolderPath, manifest.LatestSaveFile);
        if (!File.Exists(src))
            return new Plan(Status.SourceMissing, src, null, null);

        var destName = SavePicker.DownloadedSaveName(manifest.GameName, manifest.CurrentTurn);
        var dest     = Path.Combine(savesDir, destName);

        if (File.Exists(dest))
        {
            // Compare content hashes — manifest.LatestSaveHash is what we
            // *want*; if dest matches, we're already current.
            if (manifest.LatestSaveHash is not null
                && GameManifest.HashFile(dest) == manifest.LatestSaveHash)
            {
                return new Plan(Status.AlreadyHave, src, dest, destName);
            }
        }

        return new Plan(Status.Stale, src, dest, destName);
    }

    /// <summary>
    /// Execute a Stale plan — copy from shared folder to Civ saves folder.
    /// Caller must check Plan.Status == Stale first.
    /// </summary>
    public static void Execute(Plan plan)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(plan.DestPath!)!);
        File.Copy(plan.SourcePath!, plan.DestPath!, overwrite: true);
    }
}
