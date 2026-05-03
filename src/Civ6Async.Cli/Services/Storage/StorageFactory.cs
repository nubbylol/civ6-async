namespace Civ6Async.Cli.Services.Storage;

internal static class StorageFactory
{
    /// <summary>
    /// Materialize the storage backend that backs a configured game. Caller
    /// owns the returned instance — Dispose when done if it's IDisposable.
    /// </summary>
    public static IGameStorage From(LocalConfig.GameEntry entry)
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
            if (string.IsNullOrEmpty(entry.DropboxToken) || string.IsNullOrEmpty(entry.DropboxBasePath))
                throw new InvalidOperationException("Dropbox game entry missing token or base path.");
            return new DropboxStorage(entry.DropboxToken, entry.DropboxBasePath);
        }

        throw new InvalidOperationException($"Unknown storage provider: {entry.Provider}");
    }
}
