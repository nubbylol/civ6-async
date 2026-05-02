using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

internal sealed class GameHistoryCommand : Command<EmptySettings>
{
    protected override int Execute(CommandContext context, EmptySettings settings, CancellationToken cancellationToken)
    {
        var (_, manifest, err) = GameContext.Resolve();
        if (err is not null) { AnsiConsole.MarkupLine(err); return 1; }

        if (manifest!.History.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No turns submitted yet.[/]");
            return 0;
        }

        var table = new Table()
            .AddColumns("Turn", "Player", "When", "Save", "Hash")
            .Border(TableBorder.Rounded);

        foreach (var h in manifest.History)
            table.AddRow(
                h.Turn.ToString(),
                h.Player.EscapeMarkup(),
                FormatRelative(h.At),
                h.SavedAs.EscapeMarkup(),
                $"[grey]{h.Hash[..Math.Min(16, h.Hash.Length)].EscapeMarkup()}…[/]");

        AnsiConsole.Write(table);
        return 0;
    }

    private static string FormatRelative(DateTime utc)
    {
        var delta = DateTime.UtcNow - utc;
        if (delta.TotalSeconds < 60)  return "just now";
        if (delta.TotalMinutes < 60)  return $"{(int)delta.TotalMinutes}m ago";
        if (delta.TotalHours   < 24)  return $"{(int)delta.TotalHours}h ago";
        if (delta.TotalDays    < 30)  return $"{(int)delta.TotalDays}d ago";
        return utc.ToLocalTime().ToString("yyyy-MM-dd");
    }
}
