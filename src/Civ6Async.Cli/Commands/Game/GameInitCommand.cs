using System.ComponentModel;
using Civ6Async.Cli.Services;
using Civ6Async.Cli.Services.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

internal sealed class GameInitCommand : Command<GameInitCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("Name for the new game.")]
        public required string Name { get; init; }

        [CommandOption("--provider <PROVIDER>")]
        [Description("Storage provider: local (default) or r2.")]
        public string Provider { get; init; } = "local";

        [CommandOption("--shared <PATH>")]
        [Description("when --provider=local: path to the cloud-synced shared folder root (each game gets a subfolder).")]
        public string? SharedRoot { get; init; }

        [CommandOption("--r2-account-id <ID>")]
        [Description("when --provider=r2: Cloudflare account ID (visible in R2 sidebar / dashboard URL).")]
        public string? R2AccountId { get; init; }

        [CommandOption("--r2-access-key <KEY>")]
        [Description("when --provider=r2: R2 API token Access Key ID.")]
        public string? R2AccessKey { get; init; }

        [CommandOption("--r2-secret-key <SECRET>")]
        [Description("when --provider=r2: R2 API token Secret Access Key.")]
        public string? R2SecretKey { get; init; }

        [CommandOption("--r2-bucket <BUCKET>")]
        [Description("when --provider=r2: R2 bucket name.")]
        public string? R2Bucket { get; init; }

        [CommandOption("--r2-prefix <PREFIX>")]
        [Description("when --provider=r2: optional prefix root inside the bucket (default: bucket root).")]
        public string R2Prefix { get; init; } = "";

        [CommandOption("--players <CSV>")]
        [Description("Comma-separated turn order, e.g. \"arin,max,jess\".")]
        public required string PlayersCsv { get; init; }

        [CommandOption("--me <NAME>")]
        [Description("Which player you are. Must be one of --players.")]
        public required string Me { get; init; }

        [CommandOption("--webhook <URL>")]
        [Description("Optional Discord webhook URL — submits will post to this channel.")]
        public string? Webhook { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var players = settings.PlayersCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (players.Count < 2)
        {
            AnsiConsole.MarkupLine("[red]Need at least 2 players in --players.[/]");
            return 1;
        }
        if (!players.Contains(settings.Me, StringComparer.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine($"[red]--me '{settings.Me}' must be one of: {string.Join(", ", players)}[/]");
            return 1;
        }

        if (settings.Webhook is not null && !DiscordWebhook.LooksValid(settings.Webhook))
        {
            AnsiConsole.MarkupLine("[red]--webhook doesn't look like a Discord webhook URL.[/]");
            return 1;
        }

        IGameStorage    storage;
        string          providerLabel;
        Action<LocalConfig> register;

        if (settings.Provider.Equals("r2", StringComparison.OrdinalIgnoreCase))
        {
            // Pull missing credentials from the per-machine saved set —
            // typical case is "I already configured R2 once, just create
            // another game" and we shouldn't make them re-paste keys.
            var existingConfig = LocalConfig.Load();
            var accountId = settings.R2AccountId ?? existingConfig.R2AccountId;
            var accessKey = settings.R2AccessKey ?? existingConfig.R2AccessKey;
            var secretKey = settings.R2SecretKey ?? existingConfig.R2SecretKey;
            var bucket    = settings.R2Bucket    ?? existingConfig.R2Bucket;

            if (string.IsNullOrEmpty(accountId)
                || string.IsNullOrEmpty(accessKey)
                || string.IsNullOrEmpty(secretKey)
                || string.IsNullOrEmpty(bucket))
            {
                AnsiConsole.MarkupLine(
                    "[red]R2 credentials missing.[/] Pass [bold]--r2-account-id[/], [bold]--r2-access-key[/], " +
                    "[bold]--r2-secret-key[/] and [bold]--r2-bucket[/], or save them via [bold]civ6-async defaults[/].");
                return 1;
            }

            var basePrefix = string.IsNullOrEmpty(settings.R2Prefix.Trim('/'))
                ? settings.Name
                : settings.R2Prefix.Trim('/') + "/" + settings.Name;

            var s3 = new S3Storage(accountId, accessKey, secretKey, bucket, basePrefix);

            string? verify = null;
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start("Verifying R2 access…", _ => verify = s3.VerifyAccess());
            if (verify is not null)
            {
                AnsiConsole.MarkupLine($"[red]R2 access check failed:[/] {verify.EscapeMarkup()}");
                return 1;
            }

            if (s3.Exists(GameManifest.FileName))
            {
                AnsiConsole.MarkupLine(
                    $"[red]A game already exists at[/] [grey]{bucket}/{basePrefix.EscapeMarkup()}[/]. " +
                    "Use [bold]game join[/] instead, or pick a different name.");
                return 1;
            }

            storage       = s3;
            providerLabel = $"R2: {bucket}/{basePrefix}";
            register      = c =>
            {
                c.R2AccountId = accountId;
                c.R2AccessKey = accessKey;
                c.R2SecretKey = secretKey;
                c.R2Bucket    = bucket;
                c.RegisterAndActivateR2(settings.Name, basePrefix);
            };
        }
        else
        {
            if (string.IsNullOrEmpty(settings.SharedRoot))
            {
                AnsiConsole.MarkupLine("[red]--shared is required for --provider local.[/]");
                return 1;
            }

            var sharedFolder = Path.Combine(settings.SharedRoot, settings.Name);
            if (Directory.Exists(sharedFolder)
                && File.Exists(Path.Combine(sharedFolder, GameManifest.FileName)))
            {
                AnsiConsole.MarkupLine(
                    $"[red]A game already exists at[/] [grey]{sharedFolder.EscapeMarkup()}[/]. " +
                    "Use [bold]game join[/] instead, or pick a different name.");
                return 1;
            }

            storage       = new LocalFolderStorage(sharedFolder);
            providerLabel = $"local folder: {sharedFolder}";
            register      = c => c.RegisterAndActivate(settings.Name, sharedFolder);
        }

        var manifest = new GameManifest
        {
            GameName          = settings.Name,
            CreatedAt         = DateTime.UtcNow,
            Players           = players,
            CurrentPlayer     = players[0],
            CurrentTurn       = 1,
            DiscordWebhookUrl = settings.Webhook,
        };
        manifest.Save(storage);

        var config = LocalConfig.Load();
        config.PlayerName = settings.Me;
        register(config);
        config.Save();

        AnsiConsole.MarkupLine($"[green]Created game[/] [bold]{settings.Name}[/]");
        AnsiConsole.MarkupLine($"  Storage:    [grey]{providerLabel.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"  Players:    {string.Join(" → ", players)}");
        AnsiConsole.MarkupLine($"  First turn: [bold]{players[0]}[/]");
        AnsiConsole.MarkupLine($"  You:        [bold]{settings.Me}[/]");
        AnsiConsole.WriteLine();

        ModBootstrap.EnsureInstalled();
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine(
            settings.Me.Equals(players[0], StringComparison.OrdinalIgnoreCase)
                ? "It's [green]your turn[/] first. Play in Civ, save, then run [bold]civ6-async game submit[/]."
                : $"Waiting on [yellow]{players[0]}[/] to play turn 1.");

        return 0;
    }
}
