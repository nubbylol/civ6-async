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

        if (entry.Provider == "r2")
        {
            if (!config.HasR2Credentials)
                throw new InvalidOperationException(
                    "No R2 credentials configured. Run 'civ6-async defaults' or re-join the game.");
            if (entry.R2Prefix is null)
                throw new InvalidOperationException("R2 game entry missing prefix.");
            return new S3Storage(
                config.R2AccountId!,
                config.R2AccessKey!,
                config.R2SecretKey!,
                config.R2Bucket!,
                entry.R2Prefix);
        }

        throw new InvalidOperationException($"Unknown storage provider: {entry.Provider}");
    }
}
