using Civ6Async.Cli.Services.Storage;
using Dropbox.Api;
using Dropbox.Api.Files;

namespace Civ6Async.Cli.Services;

/// <summary>
/// Looks up games available under a storage root — used by the wizard's
/// join flow so users pick from a list instead of typing a path. Provider-
/// specific because IGameStorage is per-game; discovery operates on a
/// parent root.
/// </summary>
internal static class GameDiscovery
{
    public sealed record Found(string Name, string FullPath, GameManifest Manifest);

    /// <summary>
    /// Walk subfolders of <paramref name="root"/>; each one containing a
    /// turn_state.json is a discovered game.
    /// </summary>
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

    /// <summary>
    /// Lists folders directly under <paramref name="rootPath"/> in the
    /// Dropbox account the token belongs to; for each one, looks up
    /// turn_state.json and treats it as a game if present.
    /// </summary>
    public static IReadOnlyList<Found> Dropbox(string token, string rootPath)
    {
        var found = new List<Found>();
        var normalized = NormalizeBase(rootPath);

        DropboxClient? client = null;
        try
        {
            client = new DropboxClient(token);

            ListFolderResult? listing = null;
            try
            {
                listing = client.Files.ListFolderAsync(normalized).GetAwaiter().GetResult();
            }
            catch (ApiException<ListFolderError> ex) when (
                ex.ErrorResponse.IsPath && ex.ErrorResponse.AsPath.Value.IsNotFound)
            {
                // Root doesn't exist yet — no games to discover.
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
            // Token bad / network bad / etc. — caller treats empty as
            // "couldn't discover; let user paste a path manually".
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
        if (!s.StartsWith('/')) s = "/" + s;
        return s.TrimEnd('/');
    }
}
