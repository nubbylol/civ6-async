using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace Civ6Async.Cli.Services.Storage;

/// <summary>
/// S3-compatible object storage. Targets Cloudflare R2 by default — R2's
/// account-scoped endpoint is just a different ServiceURL. The same code
/// works against AWS S3, Backblaze B2, MinIO, and anything else speaking
/// the S3 API; only the endpoint URL changes.
///
/// Why R2 over Dropbox: API tokens don't expire (Dropbox killed long-lived
/// access tokens in 2021; OAuth refresh-tokens were the alternative), free
/// tier covers civ6-async use comfortably (10GB + free egress), and the
/// auth model is just a static (key, secret) pair — easier to share with
/// friends than walking each one through OAuth.
///
/// Layout: one bucket per host (or shared across hosts), each game lives
/// at a prefix inside that bucket (analogous to a Dropbox sub-folder).
/// </summary>
internal sealed class S3Storage : IGameStorage, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly string    _bucket;
    private readonly string    _prefix;  // game-relative, no leading or trailing slash

    public S3Storage(string accountId, string accessKey, string secretKey, string bucket, string prefix)
    {
        _bucket = bucket;
        _prefix = NormalizePrefix(prefix);
        var config = new AmazonS3Config
        {
            ServiceURL          = $"https://{accountId}.r2.cloudflarestorage.com",
            ForcePathStyle      = true,
            AuthenticationRegion = "auto",
        };
        _client = new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), config);
    }

    public string Description =>
        string.IsNullOrEmpty(_prefix) ? $"R2: {_bucket}" : $"R2: {_bucket}/{_prefix}";

    private string Key(string relPath)
    {
        var rel = (relPath ?? "").Replace('\\', '/').TrimStart('/');
        return string.IsNullOrEmpty(_prefix) ? rel : $"{_prefix}/{rel}";
    }

    public bool Exists(string relPath)
    {
        try
        {
            _client.GetObjectMetadataAsync(_bucket, Key(relPath)).GetAwaiter().GetResult();
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public byte[] ReadBytes(string relPath)
    {
        using var resp = _client.GetObjectAsync(_bucket, Key(relPath)).GetAwaiter().GetResult();
        using var ms   = new MemoryStream();
        resp.ResponseStream.CopyTo(ms);
        return ms.ToArray();
    }

    public void WriteBytes(string relPath, byte[] data)
    {
        using var ms = new MemoryStream(data);
        _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName  = _bucket,
            Key         = Key(relPath),
            InputStream = ms,
        }).GetAwaiter().GetResult();
    }

    public void Delete(string relPath)
    {
        try
        {
            _client.DeleteObjectAsync(_bucket, Key(relPath)).GetAwaiter().GetResult();
        }
        catch (AmazonS3Exception)
        {
            // Already gone, fine.
        }
    }

    public void UploadFile(string localPath, string relPath)
    {
        _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key        = Key(relPath),
            FilePath   = localPath,
        }).GetAwaiter().GetResult();
    }

    public void DownloadFile(string relPath, string localPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        using var resp = _client.GetObjectAsync(_bucket, Key(relPath)).GetAwaiter().GetResult();
        using var dst  = File.Create(localPath);
        resp.ResponseStream.CopyTo(dst);
    }

    public IReadOnlyList<StorageEntry> ListFiles(string relFolder = "")
    {
        var keyPrefix = string.IsNullOrEmpty(relFolder) ? _prefix : Key(relFolder);
        var entries   = new List<StorageEntry>();
        string? continuationToken = null;
        do
        {
            var resp = _client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName        = _bucket,
                Prefix            = string.IsNullOrEmpty(keyPrefix) ? null : keyPrefix + "/",
                Delimiter         = "/",
                ContinuationToken = continuationToken,
            }).GetAwaiter().GetResult();

            foreach (var obj in resp.S3Objects)
            {
                var name = Path.GetFileName(obj.Key);
                if (string.IsNullOrEmpty(name)) continue;  // folder marker
                entries.Add(new StorageEntry(name, obj.Size, obj.LastModified.ToUniversalTime()));
            }

            continuationToken = resp.IsTruncated == true ? resp.NextContinuationToken : null;
        } while (continuationToken is not null);

        return entries;
    }

    public void Wipe()
    {
        // List every object under this game's prefix and batch-delete them
        // (1000 per request — S3 cap). For a typical civ6-async game this
        // is well under one round trip.
        string? continuationToken = null;
        do
        {
            var resp = _client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName        = _bucket,
                Prefix            = string.IsNullOrEmpty(_prefix) ? null : _prefix + "/",
                ContinuationToken = continuationToken,
            }).GetAwaiter().GetResult();

            if (resp.S3Objects is { Count: > 0 } objects)
            {
                _client.DeleteObjectsAsync(new DeleteObjectsRequest
                {
                    BucketName = _bucket,
                    Objects    = objects.Select(o => new KeyVersion { Key = o.Key }).ToList(),
                }).GetAwaiter().GetResult();
            }

            continuationToken = resp.IsTruncated == true ? resp.NextContinuationToken : null;
        } while (continuationToken is not null);
    }

    /// <summary>One-shot connectivity check. Returns null on success, error message otherwise.</summary>
    public string? VerifyAccess()
    {
        try
        {
            _client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _bucket, MaxKeys = 1 })
                .GetAwaiter().GetResult();
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static string NormalizePrefix(string? p)
    {
        var s = (p ?? "").Trim().Replace('\\', '/').Trim('/');
        return s;
    }

    public void Dispose() => _client.Dispose();
}
