namespace Civ6Async.Cli.Services.Storage;

internal sealed class LocalFolderStorage : IGameStorage
{
    private readonly string _root;

    public LocalFolderStorage(string root)
    {
        _root = root;
        Directory.CreateDirectory(_root);
    }

    public string Description => $"local folder: {_root}";

    private string Resolve(string relPath) => Path.Combine(_root, relPath.Replace('/', Path.DirectorySeparatorChar));

    public bool Exists(string relPath) => File.Exists(Resolve(relPath));

    public byte[] ReadBytes(string relPath) => File.ReadAllBytes(Resolve(relPath));

    public void WriteBytes(string relPath, byte[] data)
    {
        var full = Resolve(relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        AtomicJsonWriter.WriteRaw(full, data);
    }

    public void Delete(string relPath)
    {
        var full = Resolve(relPath);
        if (File.Exists(full)) File.Delete(full);
    }

    public void UploadFile(string localPath, string relPath)
    {
        var dst = Resolve(relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.Copy(localPath, dst, overwrite: true);
    }

    public void DownloadFile(string relPath, string localPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        File.Copy(Resolve(relPath), localPath, overwrite: true);
    }

    public IReadOnlyList<StorageEntry> ListFiles(string relFolder = "")
    {
        var dir = string.IsNullOrEmpty(relFolder) ? _root : Resolve(relFolder);
        if (!Directory.Exists(dir)) return Array.Empty<StorageEntry>();
        return new DirectoryInfo(dir).EnumerateFiles()
            .Select(f => new StorageEntry(f.Name, f.Length, f.LastWriteTimeUtc))
            .ToList();
    }
}
