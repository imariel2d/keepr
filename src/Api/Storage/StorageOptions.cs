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
    /// Explicit S3 endpoint. Empty → derive R2 endpoint from AccountId.
    /// Local dev sets this to the MinIO URL (http://localhost:9000).
    /// </summary>
    public string ServiceUrl { get; set; } = "";

    public int PresignExpiryMinutes { get; set; } = 15;

    public string ResolveServiceUrl() =>
        string.IsNullOrWhiteSpace(ServiceUrl)
            ? $"https://{AccountId}.r2.cloudflarestorage.com"
            : ServiceUrl;
}
