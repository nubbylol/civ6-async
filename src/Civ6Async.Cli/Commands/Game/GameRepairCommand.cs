using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

/// <summary>
/// Diagnostic for the active game. Walks the manifest's history, verifies
/// every referenced .Civ6Save file is still in the shared folder and its
/// SHA-256 matches what the history recorded. Reports anomalies; does not
/// modify anything (read-only by design — repair is human judgment).
/// </summary>
internal sealed class GameRepairCommand : Command<EmptySettings>
{
    protected override int Execute(CommandContext context, EmptySettings settings, CancellationToken cancellationToken)
    {
        var (config, manifest, err) = GameContext.Resolve();
        if (err is not null) { AnsiConsole.MarkupLine(err); return 1; }

        var shared = config!.ActiveGameEntry!.SharedFolderPath;
        var issues = 0;

        AnsiConsole.MarkupLine($"[grey]Checking[/] [bold]{manifest!.GameName.EscapeMarkup()}[/]…");
        AnsiConsole.WriteLine();

        // 1. Latest-save reference exists + hash matches.
        if (manifest.LatestSaveFile is not null)
        {
            var path = Path.Combine(shared, manifest.LatestSaveFile);
            if (!File.Exists(path))
            {
                Report("error", $"Latest save [grey]{manifest.LatestSaveFile.EscapeMarkup()}[/] is missing from the shared folder.",
                    "Wait for cloud sync to finish, or roll back to an earlier history entry.");
                issues++;
            }
            else if (manifest.LatestSaveHash is not null
                     && GameManifest.HashFile(path) != manifest.LatestSaveHash)
            {
                Report("error", $"Latest save [grey]{manifest.LatestSaveFile.EscapeMarkup()}[/] hash does NOT match the manifest.",
                    "The file may have been edited or replaced. Re-submit or roll back.");
                issues++;
            }
        }

        // 2. History entries.
        foreach (var h in manifest.History)
        {
            var path = Path.Combine(shared, h.SavedAs);
            if (!File.Exists(path))
            {
                Report("warn", $"Turn {h.Turn} ([grey]{h.SavedAs.EscapeMarkup()}[/]) is missing in the folder.",
                    "Probably trimmed by backup retention; not a problem unless you want to roll back to that exact turn.");
                continue;
            }
            if (GameManifest.HashFile(path) != h.Hash)
            {
                Report("error", $"Turn {h.Turn} hash mismatch on [grey]{h.SavedAs.EscapeMarkup()}[/].",
                    "File contents differ from what the manifest recorded. Inspect manually.");
                issues++;
            }
        }

        // 3. Stranded files (in folder, not referenced).
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { GameManifest.FileName, SubmitLock.FileName };
        if (manifest.LatestSaveFile is not null) referenced.Add(manifest.LatestSaveFile);
        foreach (var h in manifest.History) referenced.Add(h.SavedAs);

        foreach (var file in new DirectoryInfo(shared).EnumerateFiles())
        {
            if (!file.Name.EndsWith(".Civ6Save", StringComparison.OrdinalIgnoreCase)) continue;
            if (!referenced.Contains(file.Name))
            {
                Report("info", $"Stranded save [grey]{file.Name.EscapeMarkup()}[/] in the folder isn't referenced by any history entry.",
                    "Safe to delete manually if you want a tidy folder.");
            }
        }

        // 4. Stale lock?
        var lockInfo = SubmitLock.Peek(shared);
        if (lockInfo is not null && DateTime.UtcNow - lockInfo.AcquiredAt > SubmitLock.StaleAfter)
        {
            Report("warn", $"Stale submit lock from [yellow]{lockInfo.Player.EscapeMarkup()}[/] " +
                          $"on [yellow]{lockInfo.Hostname.EscapeMarkup()}[/] " +
                          $"(acquired {(int)(DateTime.UtcNow - lockInfo.AcquiredAt).TotalMinutes}m ago).",
                "The next submit attempt will take it over automatically.");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(issues == 0
            ? "[green]No errors found.[/]"
            : $"[red]{issues} error(s) found.[/] See guidance above.");
        return issues == 0 ? 0 : 1;
    }

    private static void Report(string severity, string detail, string remediation)
    {
        var tag = severity switch
        {
            "error" => "[red]ERROR[/]",
            "warn"  => "[yellow]WARN[/]",
            _       => "[grey]info[/]",
        };
        AnsiConsole.MarkupLine($"  {tag}  {detail}");
        AnsiConsole.MarkupLine($"         [grey]→ {remediation}[/]");
    }
}
