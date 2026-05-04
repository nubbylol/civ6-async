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
///   - a copy of the host's config.json with PlayerName cleared
///
/// Send the zip to friends; they unzip, double-click the binary, the
/// first-run wizard's fast-path picks up the pre-configured game and
/// only asks them which player they are. Everything else (token,
/// folder paths, multiple games, active-game pointer) is preserved
/// so the invitee doesn't have to re-paste anything.
/// </summary>
internal sealed class GamePackCommand : Command<GamePackCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[OUTPUT]")]
        [Description("Output zip path. Default: <GameName>-invite.zip in the current directory.")]
        public string? Output { get; init; }
    }

    // Match LocalConfig's serializer options exactly so the produced
    // config.json round-trips through LocalConfig.Load on the invitee's side.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented           = true,
        DefaultIgnoreCondition  = JsonIgnoreCondition.WhenWritingNull,
    };

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (ctx, err) = GameContext.Resolve();
        if (err is not null) { AnsiConsole.MarkupLine(err); return 1; }
        var (config, manifest) = (ctx!.Config, ctx.Manifest);

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

        // Re-serialize the host's actual config — same shape Load expects —
        // with PlayerName cleared. Keeps every game, the active-game pointer,
        // R2 credentials, and folder paths intact so the invitee is one
        // prompt away from playing.
        var inviteConfig = new LocalConfig
        {
            PlayerName        = null,
            R2AccountId       = config.R2AccountId,
            R2AccessKey       = config.R2AccessKey,
            R2SecretKey       = config.R2SecretKey,
            R2Bucket          = config.R2Bucket,
            ActiveGame        = config.ActiveGame,
            Games             = config.Games,
            DefaultR2Prefix   = config.DefaultR2Prefix,
            DefaultSharedRoot = config.DefaultSharedRoot,
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

        if (config.Games.Values.Any(g => g.Provider == "r2"))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(
                "[yellow]Heads-up:[/] the zip contains your R2 access key + secret. Anyone " +
                "who gets the zip can read/write that bucket. Don't share publicly. Scope " +
                "the API token to a single bucket if you want to limit blast radius.");
        }
        return 0;
    }
}
