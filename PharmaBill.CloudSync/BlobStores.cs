using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;

namespace PharmaBill.CloudSync;

public interface IBlobStore
{
    Task<bool> ExistsAsync(string key, CancellationToken ct);
    Task PutAsync(string key, Stream content, CancellationToken ct);
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct);
}

public sealed class LocalDiskBlobStore(string root) : IBlobStore
{
    private string PathFor(string key)
    {
        var full = Path.GetFullPath(Path.Combine(root, key));
        if (!full.StartsWith(Path.GetFullPath(root), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid blob key.");
        }

        return full;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct) => Task.FromResult(File.Exists(PathFor(key)));

    public async Task PutAsync(string key, Stream content, CancellationToken ct)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await content.CopyToAsync(file, ct);
            }

            File.Move(temp, path, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        return Task.FromResult<Stream?>(File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true)
            : null);
    }
}

public sealed class S3BlobStore(IAmazonS3 client, string bucket) : IBlobStore
{
    public async Task<bool> ExistsAsync(string key, CancellationToken ct)
    {
        try
        {
            await client.GetObjectMetadataAsync(bucket, key, ct);
            return true;
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task PutAsync(string key, Stream content, CancellationToken ct) =>
        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = content,
            AutoCloseStream = false,
            UseChunkEncoding = false
        }, ct);

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
    {
        try
        {
            var response = await client.GetObjectAsync(bucket, key, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
