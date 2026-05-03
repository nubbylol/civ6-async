using Civ6Async.Cli.Services.Storage;
using Dropbox.Api;
using Dropbox.Api.Files;

namespace Civ6Async.Cli.Services;

/// <summary>
/// Looks up games available under a storage root. Used by the wizard's
/// join flow so users pick from a list instead of typing a full game-
/// folder path.
///
/// Non-recursive by design: the host puts games directly under the user-
/// supplied root (e.g. /MyGame next to App folder root for Dropbox, or
/// MyGame as a subfolder of a sync folder locally). For Dropbox App folder
/// tokens, root = "" (empty) means the App folder root itself.
/// </summary>
internal static class GameDiscovery
{
    public sealed record Found(string Name, string FullPath, GameManifest Manifest);

    public static IReadOnlyList<Found> Local(string root)
    {
        if (!Directory.Exists(root)) return Array.Empty<Found>();
        var found = new List<Found>();
        foreach (var sub in Directory.EnumerateDirectories(root))
        {
            try
            {
                var storage  = new LocalFolderStorage(sub);
                var manifest = GameManifest.TryLoad(storage);
                if (manifest is not null)
                    found.Add(new Found(Path.GetFileName(sub), sub, manifest));
            }
            catch { /* skip permission errors etc. */ }
        }
        return found;
    }

    public static IReadOnlyList<Found> Dropbox(string token, string rootPath)
    {
        var found = new List<Found>();
        var normalized = NormalizeBase(rootPath);

        DropboxClient? client = null;
        try
        {
            client = new DropboxClient(token);

            ListFolderResult? listing;
            try
            {
                listing = client.Files.ListFolderAsync(normalized).GetAwaiter().GetResult();
            }
            catch (ApiException<ListFolderError> ex) when (
                ex.ErrorResponse.IsPath && ex.ErrorResponse.AsPath.Value.IsNotFound)
            {
                return found;
            }

            while (true)
            {
                foreach (var entry in listing.Entries.OfType<FolderMetadata>())
                {
                    var subPath = entry.PathDisplay;
                    using var subStorage = new DropboxStorage(token, subPath);
                    var manifest = GameManifest.TryLoad(subStorage);
                    if (manifest is not null)
                        found.Add(new Found(entry.Name, subPath, manifest));
                }
                if (!listing.HasMore) break;
                listing = client.Files.ListFolderContinueAsync(listing.Cursor).GetAwaiter().GetResult();
            }
        }
        catch
        {
            // Bad token / network / etc. — caller falls back to manual entry.
        }
        finally
        {
            client?.Dispose();
        }
        return found;
    }

    private static string NormalizeBase(string p)
    {
        var s = p.Trim().Replace('\\', '/');
        if (string.IsNullOrEmpty(s)) return "";   // App folder root.
        if (!s.StartsWith('/')) s = "/" + s;
        return s.TrimEnd('/');
    }
}
