using Civ6Async.Cli.Services;
using Civ6Async.Cli.Services.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

internal sealed class GameRepairCommand : Command<EmptySettings>
{
    protected override int Execute(CommandContext context, EmptySettings settings, CancellationToken cancellationToken)
    {
        var (ctx, err) = GameContext.Resolve();
        if (err is not null) { AnsiConsole.MarkupLine(err); return 1; }
        var (storage, manifest) = (ctx!.Storage, ctx.Manifest);

        var issues = 0;
        AnsiConsole.MarkupLine($"[grey]Checking[/] [bold]{manifest.GameName.EscapeMarkup()}[/] [grey]({storage.Description.EscapeMarkup()})…[/]");
        AnsiConsole.WriteLine();

        if (manifest.LatestSaveFile is not null)
        {
            if (!storage.Exists(manifest.LatestSaveFile))
            {
                Report("error", $"Latest save [grey]{manifest.LatestSaveFile.EscapeMarkup()}[/] is missing from storage.",
                    "Wait for sync, or roll back to an earlier history entry.");
                issues++;
            }
            else if (manifest.LatestSaveHash is not null
                     && HashStorageFile(storage, manifest.LatestSaveFile) != manifest.LatestSaveHash)
            {
                Report("error", $"Latest save [grey]{manifest.LatestSaveFile.EscapeMarkup()}[/] hash does NOT match the manifest.",
                    "The file may have been edited or replaced. Re-submit or roll back.");
                issues++;
            }
        }

        foreach (var h in manifest.History)
        {
            if (!storage.Exists(h.SavedAs))
            {
                Report("warn", $"Turn {h.Turn} ([grey]{h.SavedAs.EscapeMarkup()}[/]) is missing in storage.",
                    "Probably trimmed by backup retention; not a problem unless you want to roll back to that turn.");
                continue;
            }
            if (HashStorageFile(storage, h.SavedAs) != h.Hash)
            {
                Report("error", $"Turn {h.Turn} hash mismatch on [grey]{h.SavedAs.EscapeMarkup()}[/].",
                    "File contents differ from what the manifest recorded.");
                issues++;
            }
        }

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { GameManifest.FileName, SubmitLock.FileName };
        if (manifest.LatestSaveFile is not null) referenced.Add(manifest.LatestSaveFile);
        foreach (var h in manifest.History) referenced.Add(h.SavedAs);

        foreach (var entry in storage.ListFiles())
        {
            if (!entry.Name.EndsWith(".Civ6Save", StringComparison.OrdinalIgnoreCase)) continue;
            if (!referenced.Contains(entry.Name))
                Report("info", $"Stranded save [grey]{entry.Name.EscapeMarkup()}[/] isn't referenced by any history entry.",
                    "Safe to delete manually if you want a tidy folder.");
        }

        var lockInfo = SubmitLock.Peek(storage);
        if (lockInfo is not null && DateTime.UtcNow - lockInfo.AcquiredAt > SubmitLock.StaleAfter)
        {
            Report("warn", $"Stale submit lock from [yellow]{lockInfo.Player.EscapeMarkup()}[/] " +
                          $"on [yellow]{lockInfo.Hostname.EscapeMarkup()}[/] " +
                          $"(acquired {(int)(DateTime.UtcNow - lockInfo.AcquiredAt).TotalMinutes}m ago).",
                "The next submit attempt will take it over automatically.");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(issues == 0 ? "[green]No errors found.[/]" : $"[red]{issues} error(s) found.[/]");
        return issues == 0 ? 0 : 1;
    }

    private static string HashStorageFile(IGameStorage storage, string relPath)
    {
        var bytes = storage.ReadBytes(relPath);
        return "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
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
