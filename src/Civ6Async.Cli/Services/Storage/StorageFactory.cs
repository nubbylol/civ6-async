namespace Civ6Async.Cli.Services.Storage;

internal static class StorageFactory
{
    /// <summary>
    /// Materialize the storage backend that backs a configured game. Caller
    /// owns the returned instance — Dispose when done if it's IDisposable.
    /// </summary>
    public static IGameStorage From(LocalConfig config, LocalConfig.GameEntry entry)
    {
        // Default / legacy: local folder.
        if (entry.Provider is null or "local")
        {
            if (string.IsNullOrEmpty(entry.SharedFolderPath))
                throw new InvalidOperationException("local-folder game entry has no SharedFolderPath.");
            return new LocalFolderStorage(entry.SharedFolderPath);
        }

        if (entry.Provider == "dropbox")
        {
            if (string.IsNullOrEmpty(config.DropboxToken))
                throw new InvalidOperationException(
                    "No Dropbox access token configured. Run 'civ6-async defaults' or re-join the game.");
            if (string.IsNullOrEmpty(entry.DropboxBasePath))
                throw new InvalidOperationException("Dropbox game entry missing base path.");
            return new DropboxStorage(config.DropboxToken, entry.DropboxBasePath);
        }

        throw new InvalidOperationException($"Unknown storage provider: {entry.Provider}");
    }
}
