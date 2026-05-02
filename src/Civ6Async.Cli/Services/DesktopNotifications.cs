using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Civ6Async.Cli.Services;

/// <summary>
/// Best-effort cross-platform desktop notifications via shell-out — keeps the
/// helper dependency-free. On platforms without a known notifier, falls back
/// silently (the calling code prints to console anyway, so the user still
/// sees the event in the watch window).
/// </summary>
internal static class DesktopNotifications
{
    public static void Show(string title, string body)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Run("notify-send", new[] { "-a", "civ6-async", title, body });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var script = $"display notification \"{Escape(body)}\" with title \"{Escape(title)}\"";
                Run("osascript", new[] { "-e", script });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows toast without a heavy NuGet: invoke PowerShell with
                // BurntToast if available, else fall back to a simple message
                // box via msg.exe (Pro/Enterprise). Both are best-effort.
                var ps = "if (Get-Module -ListAvailable -Name BurntToast) { " +
                         $"Import-Module BurntToast; New-BurntToastNotification -Text '{Escape(title)}','{Escape(body)}' " +
                         "} else { Write-Host '[civ6-async] no toast backend' }";
                Run("powershell", new[] { "-NoProfile", "-NonInteractive", "-Command", ps });
            }
        }
        catch
        {
            // Console output is the user's reliable signal; toast is bonus.
        }
    }

    private static void Run(string fileName, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName  = fileName,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi);
        proc?.WaitForExit(2000);
    }

    private static string Escape(string s) => s.Replace("\"", "\\\"").Replace("'", "''");
}
