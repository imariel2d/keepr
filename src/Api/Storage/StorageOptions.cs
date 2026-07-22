namespace Keepr.Api.Storage;

/// <summary>Bound from the "Storage" config section. See docs/ai-design-decisions.md (D1, D8).</summary>
public class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Cloudflare R2 account id (used to build the default endpoint).</summary>
    public string AccountId { get; set; } = "";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string Bucket { get; set; } = "media";

    /// <summary>
    /// Explicit S3 endpoint the API itself calls. Empty → derive R2 endpoint from AccountId.
    /// Local dev sets this to the MinIO URL (http://localhost:9000, or http://minio:9000 when
    /// the API runs in Docker).
    /// </summary>
    public string ServiceUrl { get; set; } = "";

    /// <summary>
    /// Endpoint baked into presigned URLs, i.e. the one the *browser* must be able to reach.
    /// Empty → same as <see cref="ServiceUrl"/>, which is correct for R2 and for a host-run API.
    ///
    /// These differ whenever the API reaches storage by a name the browser cannot resolve — the
    /// dockerised local stack being the case that bites: the API talks to "minio:9000" over the
    /// compose network, while the browser needs "localhost:9000". Presigning is pure local
    /// computation, so a second client pointed at the public host produces a signature that
    /// validates when the browser connects there.
    /// </summary>
    public string PublicUrl { get; set; } = "";

    public int PresignExpiryMinutes { get; set; } = 15;

    public string ResolveServiceUrl() =>
        string.IsNullOrWhiteSpace(ServiceUrl)
            ? $"https://{AccountId}.r2.cloudflarestorage.com"
            : ServiceUrl;

    /// <summary>Endpoint for presigned URLs; falls back to <see cref="ResolveServiceUrl"/>.</summary>
    public string ResolvePublicUrl() =>
        string.IsNullOrWhiteSpace(PublicUrl) ? ResolveServiceUrl() : PublicUrl;
}
