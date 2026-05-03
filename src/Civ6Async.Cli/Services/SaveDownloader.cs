using Civ6Async.Cli.Services.Storage;
using Spectre.Console;

namespace Civ6Async.Cli.Services;

internal static class SaveDownloader
{
    public enum Status
    {
        NoSaveYet,
        AlreadyHave,
        Stale,
        SavesDirMissing,
        SourceMissing,
    }

    public sealed record Plan(
        Status Status,
        string? RemoteRelPath,
        string? DestPath,
        string? DestName);

    public static Plan Inspect(IGameStorage storage, GameManifest manifest)
    {
        if (manifest.LatestSaveFile is null)
            return new Plan(Status.NoSaveYet, null, null, null);

        var savesDir = PlatformPaths.AutoDetectHotseatSavesDir();
        if (savesDir is null)
            return new Plan(Status.SavesDirMissing, null, null, null);

        if (!storage.Exists(manifest.LatestSaveFile))
            return new Plan(Status.SourceMissing, manifest.LatestSaveFile, null, null);

        var destName = SavePicker.DownloadedSaveName(manifest.GameName, manifest.CurrentTurn);
        var dest     = Path.Combine(savesDir, destName);

        if (File.Exists(dest)
            && manifest.LatestSaveHash is not null
            && GameManifest.HashFile(dest) == manifest.LatestSaveHash)
        {
            return new Plan(Status.AlreadyHave, manifest.LatestSaveFile, dest, destName);
        }

        return new Plan(Status.Stale, manifest.LatestSaveFile, dest, destName);
    }

    public static void Execute(IGameStorage storage, Plan plan)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(plan.DestPath!)!);
        storage.DownloadFile(plan.RemoteRelPath!, plan.DestPath!);
    }
}
