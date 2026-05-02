using Spectre.Console;

namespace Civ6Async.Cli.Services;

/// <summary>
/// Interactive save-file picker over the local Civ hotseat saves folder.
/// Sorted most-recently-modified first.
/// </summary>
internal static class SavePicker
{
    /// <summary>Filename prefix for saves the helper drops into the Civ saves folder via 'check'.</summary>
    public const string DownloadedSavePrefix = "civ6-async-";

    /// <summary>Builds a per-game-per-turn name, e.g. "civ6-async-MyGame-T42.Civ6Save".</summary>
    public static string DownloadedSaveName(string gameName, int turn) =>
        $"{DownloadedSavePrefix}{gameName}-T{turn:D3}.Civ6Save";

    public static string? Pick(string? savesDir)
    {
        savesDir ??= PlatformPaths.AutoDetectHotseatSavesDir();
        if (savesDir is null || !Directory.Exists(savesDir))
        {
            AnsiConsole.MarkupLine("[red]Civ 6 hotseat saves folder not found.[/]");
            return null;
        }

        var saves = new DirectoryInfo(savesDir)
            .EnumerateFiles("*.Civ6Save", SearchOption.TopDirectoryOnly)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(20)
            .ToList();

        if (saves.Count == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]No .Civ6Save files in[/] [grey]{savesDir.EscapeMarkup()}[/]");
            return null;
        }

        var labels = saves
            .Select(f => $"{f.Name}  [grey]({Format(f.LastWriteTime)})[/]")
            .Append("[red]Cancel[/]")
            .ToList();

        var pick = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Pick the save you want to submit:")
                .PageSize(15)
                .AddChoices(labels));

        if (pick.Contains("Cancel")) return null;

        var idx = labels.IndexOf(pick);
        return saves[idx].FullName;
    }

    private static string Format(DateTime t)
    {
        var delta = DateTime.Now - t;
        if (delta.TotalMinutes < 1)  return "just now";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes}m ago";
        if (delta.TotalHours   < 24) return $"{(int)delta.TotalHours}h ago";
        if (delta.TotalDays    < 7)  return $"{(int)delta.TotalDays}d ago";
        return t.ToString("yyyy-MM-dd");
    }
}
