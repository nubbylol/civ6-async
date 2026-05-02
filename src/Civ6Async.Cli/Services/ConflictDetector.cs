namespace Civ6Async.Cli.Services;

internal sealed record SubmitConflict(string Title, string Detail, string Remediation);

/// <summary>
/// All the suspicion checks we can run on a save being submitted, given only
/// file metadata + manifest state (no parsing of the save binary).
/// </summary>
internal static class ConflictDetector
{
    public static IReadOnlyList<SubmitConflict> Detect(
        GameManifest manifest,
        string playerName,
        string saveFilePath)
    {
        var conflicts = new List<SubmitConflict>();

        // 1. Wrong-player submit.
        if (!string.Equals(manifest.CurrentPlayer, playerName, StringComparison.OrdinalIgnoreCase))
        {
            conflicts.Add(new SubmitConflict(
                Title: "Not your turn",
                Detail: $"Manifest says it's [yellow]{manifest.CurrentPlayer}[/]'s turn, " +
                        $"not yours ([yellow]{playerName}[/]).",
                Remediation:
                    "Wait for them to play. If you need to override (e.g. fixing a previous bad submit), " +
                    "re-run with [bold]--force[/]."));
        }

        // 2. Identical hash — file content is byte-for-byte the previous submit.
        if (manifest.LatestSaveHash is not null)
        {
            var hash = GameManifest.HashFile(saveFilePath);
            if (hash == manifest.LatestSaveHash)
            {
                conflicts.Add(new SubmitConflict(
                    Title: "Identical to previous submit",
                    Detail: "This save's content is byte-identical to the previous submit. " +
                            "You may have picked the file we just downloaded by 'check', " +
                            "or saved without changes.",
                    Remediation:
                        "Make sure you played your turn and saved AFTERWARDS, then run submit again. " +
                        "If you really mean to resubmit the same bytes, use [bold]--force[/]."));
            }
        }

        // 3. Save mtime is older than the manifest's latest submit timestamp.
        if (manifest.LatestSaveSubmittedAt is not null)
        {
            var mtime = File.GetLastWriteTimeUtc(saveFilePath);
            if (mtime < manifest.LatestSaveSubmittedAt.Value)
            {
                conflicts.Add(new SubmitConflict(
                    Title: "Save older than the latest submit",
                    Detail: $"This save was last modified at " +
                            $"[grey]{mtime:yyyy-MM-dd HH:mm}[/] UTC, before the previous " +
                            $"submit at [grey]{manifest.LatestSaveSubmittedAt.Value:yyyy-MM-dd HH:mm}[/] UTC. " +
                            "It looks like a stale local save from before the latest turn.",
                    Remediation:
                        "Run [bold]civ6-async game check[/] to download the latest save into your Civ saves " +
                        "folder. Load [bold]civ6-async-current.Civ6Save[/] in Civilization VI, play your " +
                        "turn, save the game, then run submit again and pick the new save."));
            }
        }

        return conflicts;
    }
}
