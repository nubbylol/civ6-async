using Civ6Async.Cli.Commands.Game;
using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands;

/// <summary>
/// View and edit per-machine storage defaults — R2 credentials and the
/// folder roots the wizard pre-fills when you create or join a new game.
/// Lets users configure things once instead of typing them every time.
/// </summary>
internal sealed class DefaultsCommand : Command<EmptySettings>
{
    protected override int Execute(CommandContext context, EmptySettings settings, CancellationToken cancellationToken)
    {
        var config = LocalConfig.Load();

        AnsiConsole.MarkupLine("[bold]Storage defaults[/]");
        AnsiConsole.MarkupLine(
            "[grey]Pre-filled when the wizard creates or joins a game. " +
            "Press Enter on a prompt to keep the current value.[/]");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine($"  • R2 account ID:       {Display(config.R2AccountId, "(unset)")}");
        AnsiConsole.MarkupLine($"  • R2 access key:       {Mask(config.R2AccessKey)}");
        AnsiConsole.MarkupLine($"  • R2 secret key:       {Mask(config.R2SecretKey)}");
        AnsiConsole.MarkupLine($"  • R2 bucket:           {Display(config.R2Bucket, "(unset)")}");
        AnsiConsole.MarkupLine($"  • R2 prefix root:      {Display(config.DefaultR2Prefix, "bucket root")}");
        AnsiConsole.MarkupLine($"  • Local folder root:   {Display(config.DefaultSharedRoot, "(unset)")}");
        AnsiConsole.WriteLine();

        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What do you want to change?")
                .AddChoices(
                    "R2 credentials (account ID, access key, secret, bucket)",
                    "R2 prefix root",
                    "Local folder root",
                    "Cancel"));

        switch (action)
        {
            case "R2 credentials (account ID, access key, secret, bucket)":
                EditR2Credentials(config);
                break;

            case "R2 prefix root":
                config.DefaultR2Prefix = AnsiConsole.Prompt(
                    new TextPrompt<string>("R2 prefix root (blank for bucket root):")
                        .AllowEmpty()
                        .DefaultValue(config.DefaultR2Prefix ?? ""));
                break;

            case "Local folder root":
                var entered = AnsiConsole.Prompt(
                    new TextPrompt<string>("Local folder root (blank to clear):")
                        .AllowEmpty()
                        .DefaultValue(config.DefaultSharedRoot ?? ""));
                config.DefaultSharedRoot = string.IsNullOrWhiteSpace(entered) ? null : entered;
                break;

            default:
                AnsiConsole.MarkupLine("[grey]No changes.[/]");
                return 0;
        }

        config.Save();
        AnsiConsole.MarkupLine("[green]Saved.[/]");
        return 0;
    }

    private static void EditR2Credentials(LocalConfig config)
    {
        AnsiConsole.MarkupLine("[grey]Press Enter on any field to keep the current value. " +
                               "Type 'clear' on a field to remove it.[/]");

        config.R2AccountId = PromptOrKeep("Cloudflare account ID:", config.R2AccountId, secret: false);
        config.R2AccessKey = PromptOrKeep("R2 Access Key ID:",     config.R2AccessKey, secret: false);
        config.R2SecretKey = PromptOrKeep("R2 Secret Access Key:", config.R2SecretKey, secret: true);
        config.R2Bucket    = PromptOrKeep("R2 bucket name:",       config.R2Bucket,    secret: false);
    }

    private static string? PromptOrKeep(string label, string? current, bool secret)
    {
        var prompt = new TextPrompt<string>(label).AllowEmpty();
        if (secret) prompt.Secret('*');
        if (!string.IsNullOrEmpty(current)) prompt.DefaultValue(current);
        else                                 prompt.DefaultValue("");

        var entered = AnsiConsole.Prompt(prompt);
        if (string.IsNullOrWhiteSpace(entered)) return current;
        if (entered.Equals("clear", StringComparison.OrdinalIgnoreCase)) return null;
        return entered;
    }

    private static string Display(string? value, string fallback) =>
        string.IsNullOrEmpty(value) ? $"[grey]{fallback}[/]" : $"[bold]{value.EscapeMarkup()}[/]";

    private static string Mask(string? token)
    {
        if (string.IsNullOrEmpty(token)) return "[grey](unset)[/]";
        var tail = token.Length <= 4 ? token : token[^4..];
        return $"[bold]****{tail.EscapeMarkup()}[/]";
    }
}
