using System.Net.Http.Json;

namespace Civ6Async.Cli.Services;

/// <summary>
/// Posts a single text message to a Discord webhook URL. Failure is
/// best-effort logged; we never fail a submit because Discord is down.
/// </summary>
internal static class DiscordWebhook
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public static async Task<bool> PostAsync(string webhookUrl, string content)
    {
        try
        {
            var payload = new { content };
            var resp    = await Http.PostAsJsonAsync(webhookUrl, payload);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static bool LooksValid(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && u.Scheme == Uri.UriSchemeHttps
        && (u.Host == "discord.com" || u.Host == "discordapp.com" || u.Host == "ptb.discord.com");
}
