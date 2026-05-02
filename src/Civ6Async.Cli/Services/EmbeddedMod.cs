using System.Reflection;

namespace Civ6Async.Cli.Services;

/// <summary>
/// Mod files (.modinfo + UI/*) embedded into the binary at build time. Each
/// entry maps a relative path inside the target install folder to the
/// resource name in the assembly manifest.
/// </summary>
internal static class EmbeddedMod
{
    public const string ModFolderName = "civ6-async";

    public static readonly IReadOnlyList<EmbeddedFile> Files = new[]
    {
        new EmbeddedFile("civ6-async.modinfo",        "ModAssets/civ6-async.modinfo"),
        new EmbeddedFile("UI/PlayerChange.lua",       "ModAssets/UI/PlayerChange.lua"),
        new EmbeddedFile("UI/ActionPanel.lua",        "ModAssets/UI/ActionPanel.lua"),
        new EmbeddedFile("UI/ForceAutoEndTurn.lua",   "ModAssets/UI/ForceAutoEndTurn.lua"),
        new EmbeddedFile("UI/ForceAutoEndTurn.xml",   "ModAssets/UI/ForceAutoEndTurn.xml"),
    };

    public static byte[] Read(string resourceName)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found. Build issue?");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}

internal sealed record EmbeddedFile(string RelativePath, string ResourceName);
