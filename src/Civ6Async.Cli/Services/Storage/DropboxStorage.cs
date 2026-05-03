using Dropbox.Api;
using Dropbox.Api.Files;

namespace Civ6Async.Cli.Services.Storage;

/// <summary>
/// Direct HTTPS to Dropbox via the official SDK. Bypasses any desktop sync
/// client. Backed by a single host-issued access token (Option A from the
/// design discussion) — every player's helper authenticates with that
/// shared token, reading and writing into a single Dropbox folder owned
/// by the host. Trade-off: anyone with the join link can write to the
/// folder. Acceptable for a friend-group game.
/// </summary>
internal sealed class DropboxStorage : IGameStorage, IDisposable
{
    private readonly DropboxClient _client;
    private readonly string        _basePath;  // always starts with '/' and never ends with one

    public DropboxStorage(string accessToken, string basePath)
    {
        _client   = new DropboxClient(accessToken);
        _basePath = NormalizeBase(basePath);
    }

    public string Description => $"Dropbox: {_basePath}";

    private static string NormalizeBase(string p)
    {
        var s = p.Trim().Replace('\\', '/');
        if (!s.StartsWith('/')) s = "/" + s;
        return s.TrimEnd('/');
    }

    private string Resolve(string relPath)
    {
        if (string.IsNullOrEmpty(relPath)) return _basePath;
        var rel = relPath.Replace('\\', '/').TrimStart('/');
        return $"{_basePath}/{rel}";
    }

    public bool Exists(string relPath)
    {
        try
        {
            _client.Files.GetMetadataAsync(Resolve(relPath)).GetAwaiter().GetResult();
            return true;
        }
        catch (ApiException<GetMetadataError>)
        {
            return false;
        }
    }

    public byte[] ReadBytes(string relPath)
    {
        using var resp = _client.Files.DownloadAsync(Resolve(relPath)).GetAwaiter().GetResult();
        return resp.GetContentAsByteArrayAsync().GetAwaiter().GetResult();
    }

    public void WriteBytes(string relPath, byte[] data)
    {
        using var ms = new MemoryStream(data);
        _client.Files.UploadAsync(
                Resolve(relPath),
                WriteMode.Overwrite.Instance,
                body: ms)
            .GetAwaiter().GetResult();
    }

    public void Delete(string relPath)
    {
        try
        {
            _client.Files.DeleteV2Async(Resolve(relPath)).GetAwaiter().GetResult();
        }
        catch (ApiException<DeleteError>)
        {
            // Already gone, fine.
        }
    }

    public void UploadFile(string localPath, string relPath)
    {
        using var fs = File.OpenRead(localPath);
        _client.Files.UploadAsync(
                Resolve(relPath),
                WriteMode.Overwrite.Instance,
                body: fs)
            .GetAwaiter().GetResult();
    }

    public void DownloadFile(string relPath, string localPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        using var resp = _client.Files.DownloadAsync(Resolve(relPath)).GetAwaiter().GetResult();
        using var src  = resp.GetContentAsStreamAsync().GetAwaiter().GetResult();
        using var dst  = File.Create(localPath);
        src.CopyTo(dst);
    }

    public IReadOnlyList<StorageEntry> ListFiles(string relFolder = "")
    {
        var path = Resolve(relFolder);
        try
        {
            var resp = _client.Files.ListFolderAsync(path).GetAwaiter().GetResult();
            var entries = new List<StorageEntry>();
            while (true)
            {
                foreach (var e in resp.Entries.OfType<FileMetadata>())
                {
                    entries.Add(new StorageEntry(
                        e.Name,
                        (long)e.Size,
                        e.ServerModified.ToUniversalTime()));
                }
                if (!resp.HasMore) break;
                resp = _client.Files.ListFolderContinueAsync(resp.Cursor).GetAwaiter().GetResult();
            }
            return entries;
        }
        catch (ApiException<ListFolderError>)
        {
            return Array.Empty<StorageEntry>();
        }
    }

    public void Wipe()
    {
        try
        {
            _client.Files.DeleteV2Async(_basePath).GetAwaiter().GetResult();
        }
        catch (ApiException<DeleteError>)
        {
            // Folder already gone — fine.
        }
    }

    /// <summary>One-shot connectivity check. Returns null on success or the error message.</summary>
    public string? VerifyAccess()
    {
        try
        {
            // Try to list the base folder; create it if missing.
            try
            {
                _client.Files.ListFolderAsync(_basePath).GetAwaiter().GetResult();
            }
            catch (ApiException<ListFolderError> ex) when (
                ex.ErrorResponse.IsPath
                && ex.ErrorResponse.AsPath.Value.IsNotFound)
            {
                _client.Files.CreateFolderV2Async(_basePath).GetAwaiter().GetResult();
            }
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public void Dispose() => _client.Dispose();
}
