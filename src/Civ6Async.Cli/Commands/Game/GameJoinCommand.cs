using System.ComponentModel;
using Civ6Async.Cli.Services;
using Civ6Async.Cli.Services.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands.Game;

internal sealed class GameJoinCommand : Command<GameJoinCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--shared <PATH>")]
        [Description("when joining a local-folder game: full path to the existing game's shared folder.")]
        public string? SharedFolder { get; init; }

        [CommandOption("--r2-account-id <ID>")]
        [Description("when joining an R2 game: Cloudflare account ID.")]
        public string? R2AccountId { get; init; }

        [CommandOption("--r2-access-key <KEY>")]
        [Description("when joining an R2 game: R2 API token Access Key ID.")]
        public string? R2AccessKey { get; init; }

        [CommandOption("--r2-secret-key <SECRET>")]
        [Description("when joining an R2 game: R2 API token Secret Access Key.")]
        public string? R2SecretKey { get; init; }

        [CommandOption("--r2-bucket <BUCKET>")]
        [Description("when joining an R2 game: R2 bucket name.")]
        public string? R2Bucket { get; init; }

        [CommandOption("--r2-prefix <PREFIX>")]
        [Description("when joining an R2 game: full prefix of the existing game inside the bucket, e.g. \"MyGame\" or \"season-2/MyGame\".")]
        public string? R2Prefix { get; init; }

        [CommandOption("--me <NAME>")]
        [Description("Which player you are. Picker if omitted.")]
        public string? Me { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var existingConfig = LocalConfig.Load();
        IGameStorage storage;
        Action<LocalConfig, string> register;

        // R2 credentials: explicit flags win; otherwise per-machine saved values.
        var accountId = settings.R2AccountId ?? existingConfig.R2AccountId;
        var accessKey = settings.R2AccessKey ?? existingConfig.R2AccessKey;
        var secretKey = settings.R2SecretKey ?? existingConfig.R2SecretKey;
        var bucket    = settings.R2Bucket    ?? existingConfig.R2Bucket;

        var hasR2 = !string.IsNullOrEmpty(accountId)
                    && !string.IsNullOrEmpty(accessKey)
                    && !string.IsNullOrEmpty(secretKey)
                    && !string.IsNullOrEmpty(bucket)
                    && !string.IsNullOrEmpty(settings.R2Prefix);

        if (hasR2)
        {
            var s3 = new S3Storage(accountId!, accessKey!, secretKey!, bucket!, settings.R2Prefix!);

            string? verify = null;
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start("Verifying R2 access…", _ => verify = s3.VerifyAccess());
            if (verify is not null)
            {
                AnsiConsole.MarkupLine($"[red]R2 access check failed:[/] {verify.EscapeMarkup()}");
                return 1;
            }
            storage  = s3;
            register = (cfg, gameName) =>
            {
                cfg.R2AccountId = accountId;
                cfg.R2AccessKey = accessKey;
                cfg.R2SecretKey = secretKey;
                cfg.R2Bucket    = bucket;
                cfg.RegisterAndActivateR2(gameName, settings.R2Prefix!);
            };
        }
        else if (!string.IsNullOrEmpty(settings.SharedFolder))
        {
            storage  = new LocalFolderStorage(settings.SharedFolder);
            register = (cfg, gameName) => cfg.RegisterAndActivate(gameName, settings.SharedFolder);
        }
        else
        {
            AnsiConsole.MarkupLine(
                "[red]Need either --shared <path> (local) or the --r2-* args (R2). " +
                "If you've already saved R2 credentials, just pass --r2-prefix.[/]");
            return 1;
        }

        var manifest = GameManifest.TryLoad(storage);
        if (manifest is null)
        {
            AnsiConsole.MarkupLine(
                $"[red]No game manifest found at[/] [grey]{storage.Description.EscapeMarkup()}[/]. " +
                "Wait for the host to create the game with [bold]game init[/], or check the path / credentials.");
            return 1;
        }

        var me = settings.Me ?? AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Which player are you?")
                .AddChoices(manifest.Players));

        if (!manifest.Players.Contains(me, StringComparer.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine(
                $"[red]'{me}' is not a player in this game.[/] Players: {string.Join(", ", manifest.Players)}");
            return 1;
        }

        var config = LocalConfig.Load();
        config.PlayerName = me;
        register(config, manifest.GameName);
        config.Save();

        AnsiConsole.MarkupLine($"[green]Joined[/] [bold]{manifest.GameName}[/] as [bold]{me}[/].");
        AnsiConsole.MarkupLine($"Currently on turn [bold]{manifest.CurrentTurn}[/], waiting on [yellow]{manifest.CurrentPlayer}[/].");
        AnsiConsole.WriteLine();

        ModBootstrap.EnsureInstalled();
        return 0;
    }
}
