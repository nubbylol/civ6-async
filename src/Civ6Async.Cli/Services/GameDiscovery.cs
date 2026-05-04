using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Civ6Async.Cli.Services.Storage;

namespace Civ6Async.Cli.Services;

/// <summary>
/// Looks up games available under a storage root. Used by the wizard's
/// join flow so users pick from a list instead of typing a full per-game
/// path.
///
/// Non-recursive by design: the host puts games directly under the user-
/// supplied root — a subfolder for local, a top-level prefix for R2. The
/// "root" can be the bucket itself (empty prefix) or a sub-prefix the
/// host uses to group games (e.g. "season-2/").
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

    public static IReadOnlyList<Found> R2(
        string accountId, string accessKey, string secretKey, string bucket, string rootPrefix)
    {
        var found     = new List<Found>();
        var normalized = NormalizePrefix(rootPrefix);

        AmazonS3Client? client = null;
        try
        {
            var config = new AmazonS3Config
            {
                ServiceURL          = $"https://{accountId}.r2.cloudflarestorage.com",
                ForcePathStyle      = true,
                AuthenticationRegion = "auto",
            };
            client = new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), config);

            // List immediate "subfolders" of normalized via the delimiter trick:
            // S3 returns CommonPrefixes for each unique segment up to the
            // delimiter — that's our list of candidate game prefixes.
            string? continuationToken = null;
            var prefixes = new List<string>();
            do
            {
                var resp = client.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName        = bucket,
                    Prefix            = string.IsNullOrEmpty(normalized) ? null : normalized + "/",
                    Delimiter         = "/",
                    ContinuationToken = continuationToken,
                }).GetAwaiter().GetResult();

                if (resp.CommonPrefixes is { } cp) prefixes.AddRange(cp);
                continuationToken = resp.IsTruncated == true ? resp.NextContinuationToken : null;
            } while (continuationToken is not null);

            foreach (var prefix in prefixes)
            {
                // CommonPrefix is like "MyGame/" — strip trailing slash to get prefix.
                var clean   = prefix.TrimEnd('/');
                var name    = string.IsNullOrEmpty(normalized)
                    ? clean
                    : clean.Substring(normalized.Length + 1);  // strip "<root>/" leading
                using var subStorage = new S3Storage(accountId, accessKey, secretKey, bucket, clean);
                var manifest = GameManifest.TryLoad(subStorage);
                if (manifest is not null)
                    found.Add(new Found(name, clean, manifest));
            }
        }
        catch
        {
            // Bad credentials / network / etc. — caller falls back to manual entry.
        }
        finally
        {
            client?.Dispose();
        }
        return found;
    }

    private static string NormalizePrefix(string p)
    {
        var s = (p ?? "").Trim().Replace('\\', '/').Trim('/');
        return s;
    }
}
