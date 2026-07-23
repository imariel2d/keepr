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
    /// <summary>
    /// Used only to mint presigned URLs. Identical to <see cref="_s3"/> unless Storage:PublicUrl
    /// says the browser must reach storage at a different host than the API does — the dockerised
    /// local stack, where the API uses "minio:9000" but the browser can only resolve
    /// "localhost:9000". Signing is offline, so this client never opens a connection.
    /// </summary>
    private readonly IAmazonS3 _presignS3;
    private readonly StorageOptions _opt;
    private readonly bool _forceHttp;
    private readonly bool _presignForceHttp;

    public R2ObjectStorage(IOptions<StorageOptions> opt)
    {
        _opt = opt.Value;
        var serviceUrl = _opt.ResolveServiceUrl();
        var publicUrl = _opt.ResolvePublicUrl();

        _forceHttp = serviceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        _presignForceHttp = publicUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

        _s3 = BuildClient(serviceUrl, _forceHttp);
        _presignS3 = string.Equals(serviceUrl, publicUrl, StringComparison.OrdinalIgnoreCase)
            ? _s3
            : BuildClient(publicUrl, _presignForceHttp);
    }

    private AmazonS3Client BuildClient(string serviceUrl, bool useHttp) =>
        new(_opt.AccessKey, _opt.SecretKey, new AmazonS3Config
        {
            ServiceURL = serviceUrl,
            ForcePathStyle = true,          // required for MinIO and simplest for R2
            AuthenticationRegion = "auto",  // R2 expects region "auto"
            UseHttp = useHttp
        });

    // The SDK v4 endpoint resolver emits https for custom endpoints even when UseHttp is set,
    // so presigned URLs for a plain-HTTP endpoint (MinIO in dev) must be downgraded to match.
    private string MatchScheme(string url) =>
        _presignForceHttp && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
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
        MatchScheme(await _presignS3.GetPreSignedURLAsync(new GetPreSignedUrlRequest
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

    public async Task<string> PresignGetUrlAsync(
        string key, PresignHeaders headers = default, CancellationToken ct = default)
    {
        var req = new GetPreSignedUrlRequest
        {
            BucketName = _opt.Bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(_opt.PresignExpiryMinutes)
        };

        // Sent as response-content-* query parameters, which S3/MinIO echo back as response
        // headers. They are part of the signature, so a client cannot tamper with them.
        var disposition = headers.BuildContentDisposition();
        if (disposition is not null || headers.ContentType is not null)
        {
            req.ResponseHeaderOverrides.ContentDisposition = disposition;
            req.ResponseHeaderOverrides.ContentType = headers.ContentType;
        }

        return MatchScheme(await _presignS3.GetPreSignedURLAsync(req));
    }

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
