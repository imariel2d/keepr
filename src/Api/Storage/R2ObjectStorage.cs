using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Keepr.Api.Storage;

/// <summary>
/// S3-API implementation targeting Cloudflare R2 (or MinIO in dev). Path-style addressing and
/// region "auto" are used so the same client works against both. See ai-design-decisions.md (D1).
/// </summary>
public sealed class R2ObjectStorage : IObjectStorage
{
    private readonly IAmazonS3 _s3;
    private readonly StorageOptions _opt;
    private readonly bool _forceHttp;

    public R2ObjectStorage(IOptions<StorageOptions> opt)
    {
        _opt = opt.Value;
        var serviceUrl = _opt.ResolveServiceUrl();
        _forceHttp = serviceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        var config = new AmazonS3Config
        {
            ServiceURL = serviceUrl,
            ForcePathStyle = true,          // required for MinIO and simplest for R2
            AuthenticationRegion = "auto",  // R2 expects region "auto"
            UseHttp = _forceHttp
        };
        _s3 = new AmazonS3Client(_opt.AccessKey, _opt.SecretKey, config);
    }

    // The SDK v4 endpoint resolver emits https for custom endpoints even when UseHttp is set,
    // so presigned URLs for a plain-HTTP endpoint (MinIO in dev) must be downgraded to match.
    private string MatchScheme(string url) =>
        _forceHttp && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? string.Concat("http://", url.AsSpan("https://".Length))
            : url;

    public async Task<string> CreateMultipartUploadAsync(string key, string? contentType, CancellationToken ct = default)
    {
        var res = await _s3.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest
        {
            BucketName = _opt.Bucket,
            Key = key,
            ContentType = contentType
        }, ct);
        return res.UploadId;
    }

    public async Task<string> PresignUploadPartUrlAsync(string key, string uploadId, int partNumber, CancellationToken ct = default) =>
        MatchScheme(await _s3.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _opt.Bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            UploadId = uploadId,
            PartNumber = partNumber,
            Expires = DateTime.UtcNow.AddMinutes(_opt.PresignExpiryMinutes)
        }));

    public async Task<long> CompleteMultipartUploadAsync(
        string key, string uploadId, IReadOnlyList<CompletedPart> parts, CancellationToken ct = default)
    {
        await _s3.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
        {
            BucketName = _opt.Bucket,
            Key = key,
            UploadId = uploadId,
            PartETags = parts
                .OrderBy(p => p.PartNumber)
                .Select(p => new PartETag(p.PartNumber, p.ETag))
                .ToList()
        }, ct);

        // Trust storage, not the client: read back the real size.
        var head = await _s3.GetObjectMetadataAsync(_opt.Bucket, key, ct);
        return head.ContentLength;
    }

    public Task AbortMultipartUploadAsync(string key, string uploadId, CancellationToken ct = default) =>
        _s3.AbortMultipartUploadAsync(new AbortMultipartUploadRequest
        {
            BucketName = _opt.Bucket,
            Key = key,
            UploadId = uploadId
        }, ct);

    public async Task<string> PresignGetUrlAsync(string key, CancellationToken ct = default) =>
        MatchScheme(await _s3.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _opt.Bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(_opt.PresignExpiryMinutes)
        }));

    public async Task<byte[]> ReadHeadBytesAsync(string key, int count, CancellationToken ct = default)
    {
        var req = new GetObjectRequest
        {
            BucketName = _opt.Bucket,
            Key = key,
            ByteRange = new ByteRange(0, count - 1)
        };
        using var res = await _s3.GetObjectAsync(req, ct);
        using var ms = new MemoryStream();
        await res.ResponseStream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    public Task DeleteAsync(string key, CancellationToken ct = default) =>
        _s3.DeleteObjectAsync(_opt.Bucket, key, ct);

    public async Task DeleteManyAsync(IReadOnlyList<string> keys, CancellationToken ct = default)
    {
        // S3/R2 cap DeleteObjects at 1000 keys per request.
        const int chunkSize = 1000;

        for (var i = 0; i < keys.Count; i += chunkSize)
        {
            var chunk = keys.Skip(i).Take(chunkSize).Select(k => new KeyVersion { Key = k }).ToList();
            await _s3.DeleteObjectsAsync(
                new DeleteObjectsRequest { BucketName = _opt.Bucket, Objects = chunk }, ct);
        }
    }
}
