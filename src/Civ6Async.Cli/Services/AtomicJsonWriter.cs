using System.Text.Json;

namespace Civ6Async.Cli.Services;

/// <summary>
/// Writes JSON to <paramref name="path"/> atomically: serialize to a sibling
/// .tmp file, then rename over the destination. Cloud-sync clients (Dropbox,
/// Drive, OneDrive) can otherwise observe a half-written manifest mid-flush
/// and either upload the partial bytes or refuse to read the file. The
/// rename is atomic on every platform .NET 8 supports.
/// </summary>
internal static class AtomicJsonWriter
{
    public static void Write<T>(string path, T value, JsonSerializerOptions options)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(value, options);
        WriteRaw(path, json);
    }

    public static void WriteRaw(string path, byte[] bytes)
    {
        var tmp = path + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            throw;
        }
    }
}
