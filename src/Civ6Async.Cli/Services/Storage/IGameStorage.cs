namespace Civ6Async.Cli.Services.Storage;

/// <summary>
/// Storage layer for a game's shared state. Two implementations:
///   - LocalFolderStorage: classic shared folder on disk (Dropbox/Drive
///     desktop / Syncthing / NAS / whatever syncs files into a path).
///   - DropboxStorage: direct HTTPS calls to the Dropbox API, bypassing
///     any desktop sync client.
///
/// All paths are forward-slashed and relative to the game's root.
/// Implementations are responsible for translating to backend-specific
/// path conventions.
///
/// Sync-first: callers stay synchronous. Async backends (Dropbox) wrap
/// with .GetAwaiter().GetResult() internally — fine for a CLI tool that
/// runs one operation at a time.
/// </summary>
internal interface IGameStorage
{
    /// <summary>Human-readable identifier (shown in status / errors).</summary>
    string Description { get; }

    bool   Exists(string relPath);
    byte[] ReadBytes(string relPath);
    void   WriteBytes(string relPath, byte[] data);
    void   Delete(string relPath);

    /// <summary>Upload a local file into the shared store at the given relative path.</summary>
    void UploadFile(string localPath, string relPath);

    /// <summary>Download a file from the shared store to a local path.</summary>
    void DownloadFile(string relPath, string localPath);

    /// <summary>List files at the top of <paramref name="relFolder"/>. Empty for none.</summary>
    IReadOnlyList<StorageEntry> ListFiles(string relFolder = "");
}

internal sealed record StorageEntry(string Name, long Size, DateTime ModifiedUtc);
