using System.ComponentModel;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

/// <summary>
/// Produces a single zip containing:
///   - the running civ6-async binary
///   - a stripped-down config.json (active game's storage entry, no playerName)
///
/// Send the zip to friends; they unzip, double-click the binary, the
/// first-run wizard's fast-path picks up the pre-configured game and
/// only asks them which player they are.
/// </summary>
internal sealed class GamePackCommand : Command<GamePackCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[OUTPUT]")]
        [Description("Output zip path. Default: <GameName>-invite.zip in the current directory.")]
        public string? Output { get; init; }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (ctx, err) = GameContext.Resolve();
        if (err is not null) { AnsiConsole.MarkupLine(err); return 1; }
        var (config, manifest) = (ctx!.Config, ctx.Manifest);
        var entry = config.ActiveGameEntry!;

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            AnsiConsole.MarkupLine("[red]Couldn't locate the running binary.[/] Run from a published .exe / binary, not 'dotnet run'.");
            return 1;
        }

        var output = settings.Output ?? $"{manifest.GameName}-invite.zip";
        if (File.Exists(output))
        {
            if (!AnsiConsole.Confirm($"{output} already exists. Overwrite?"))
            {
                AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
                return 1;
            }
            File.Delete(output);
        }

        // Stripped config: only the active game's entry, marked active, no playerName.
        var inviteConfig = new InviteConfig
        {
            ActiveGame = manifest.GameName,
            Games = new Dictionary<string, LocalConfig.GameEntry>
            {
                [manifest.GameName] = entry,
            },
        };
        var inviteJson = JsonSerializer.SerializeToUtf8Bytes(inviteConfig, JsonOpts);

        try
        {
            using var zip = ZipFile.Open(output, ZipArchiveMode.Create);
            zip.CreateEntryFromFile(exePath, Path.GetFileName(exePath), CompressionLevel.Optimal);
            var configEntry = zip.CreateEntry("config.json", CompressionLevel.Optimal);
            using (var s = configEntry.Open()) s.Write(inviteJson, 0, inviteJson.Length);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Couldn't write zip:[/] {ex.Message.EscapeMarkup()}");
            return 1;
        }

        var size = new FileInfo(output).Length;
        AnsiConsole.MarkupLine($"[green]Wrote[/] [bold]{output.EscapeMarkup()}[/] ({size / (1024 * 1024)} MB)");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Send this zip to invitees. They unzip, double-click the binary, and the[/]");
        AnsiConsole.MarkupLine("[grey]wizard will skip straight to \"which player are you?\".[/]");

        if (entry.Provider == "dropbox")
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(
                "[yellow]Heads-up:[/] the zip contains your Dropbox access token. Anyone " +
                "who gets the zip can read/write the game folder. Don't share it publicly.");
        }
        return 0;
    }

    /// <summary>
    /// Subset of LocalConfig — drop PlayerName so each invitee gets a fresh
    /// "which player are you?" prompt.
    /// </summary>
    private sealed class InviteConfig
    {
        [JsonPropertyName("activeGame")]
        public string? ActiveGame { get; set; }
        [JsonPropertyName("games")]
        public Dictionary<string, LocalConfig.GameEntry> Games { get; set; } = new();
    }
}
