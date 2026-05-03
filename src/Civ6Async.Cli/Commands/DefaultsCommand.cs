using Civ6Async.Cli.Commands.Game;
using Civ6Async.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Civ6Async.Cli.Commands;

/// <summary>
/// View and edit per-machine storage defaults — the folder roots and
/// Dropbox token the wizard pre-fills when you create or join a new
/// game. Lets users configure things once instead of typing them every
/// time.
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

        AnsiConsole.MarkupLine($"  • Dropbox token:      {Mask(config.DropboxToken)}");
        AnsiConsole.MarkupLine($"  • Dropbox root folder: {Display(config.DefaultDropboxRoot, "App folder root")}");
        AnsiConsole.MarkupLine($"  • Local folder root:   {Display(config.DefaultSharedRoot, "(unset)")}");
        AnsiConsole.WriteLine();

        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What do you want to change?")
                .AddChoices(
                    "Dropbox token",
                    "Dropbox root folder",
                    "Local folder root",
                    "Cancel"));

        switch (action)
        {
            case "Dropbox token":
                config.DropboxToken = PromptToken(config.DropboxToken);
                break;
            case "Dropbox root folder":
                config.DefaultDropboxRoot = AnsiConsole.Prompt(
                    new TextPrompt<string>("Dropbox root folder:")
                        .AllowEmpty()
                        .DefaultValue(config.DefaultDropboxRoot ?? ""));
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

    private static string Display(string? value, string fallback) =>
        string.IsNullOrEmpty(value) ? $"[grey]{fallback}[/]" : $"[bold]{value.EscapeMarkup()}[/]";

    private static string Mask(string? token)
    {
        if (string.IsNullOrEmpty(token)) return "[grey](unset)[/]";
        var tail = token.Length <= 4 ? token : token[^4..];
        return $"[bold]****{tail.EscapeMarkup()}[/]";
    }

    private static string? PromptToken(string? current)
    {
        AnsiConsole.MarkupLine(
            string.IsNullOrEmpty(current)
                ? "[grey]Paste your Dropbox access token. Get one at https://www.dropbox.com/developers/apps[/]"
                : "[grey]Paste a new token to replace the saved one, or type 'clear' to remove.[/]");
        var entered = AnsiConsole.Prompt(
            new TextPrompt<string>("Dropbox token:")
                .AllowEmpty()
                .Secret('*'));

        if (string.IsNullOrWhiteSpace(entered)) return current;
        if (entered.Equals("clear", StringComparison.OrdinalIgnoreCase)) return null;
        return entered;
    }
}
