using Keepr.Api.Services;
using Keepr.Api.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Keepr.Api.Features.Sharing;

/// <summary>What the public viewer page needs to render — and nothing more. No owner identity, no
/// folder, no internal file id: a link discloses exactly the one file it is for.</summary>
public record SharePublicResponse(
    string FileName, string? ContentType, long SizeBytes, string? PreviewKind, DateTimeOffset ExpiresAt);

/// <summary>A short-TTL presigned URL for the shared file, minted fresh per request.</summary>
/// <param name="Url">The presigned storage URL to fetch the bytes from directly.</param>
/// <param name="ExpiresAt">When the URL stops working, so the client can cache it without parsing
/// the signature's own expiry fields.</param>
public record ShareDownloadUrlResponse(string Url, DateTimeOffset ExpiresAt);

/// <summary>
/// The anonymous read side of a shareable link. The token in the URL is the whole authorization;
/// there is no account here. These are the app's first unauthenticated content endpoints.
/// See docs/shareable-links-design.md.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/share")]
public class PublicShareController(
    ShareLinkService shares,
    IObjectStorage storage,
    IOptions<StorageOptions> storageOptions) : ControllerBase
{
    private DateTimeOffset PresignExpiry =>
        DateTimeOffset.UtcNow.AddMinutes(storageOptions.Value.PresignExpiryMinutes);

    /// <summary>
    /// Resolve a link to the metadata the viewer renders. <c>404</c> for an unknown token,
    /// <c>410 Gone</c> when the link is expired, revoked, or its file was removed.
    /// </summary>
    [HttpGet("{token}")]
    [ProducesResponseType<SharePublicResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    public async Task<IActionResult> Resolve(string token, CancellationToken ct)
    {
        var result = await shares.ResolveAsync(token, ct);
        if (result.Status == ShareResolveStatus.NotFound) return LinkNotFound();
        if (result.Status == ShareResolveStatus.Gone) return LinkUnavailable();

        var file = result.File!;
        return Ok(new SharePublicResponse(
            file.OriginalName, file.ContentType, file.SizeBytes,
            PreviewPolicy.KindName(file.ContentType), result.Link!.ExpiresAt));
    }

    /// <summary>
    /// A short-TTL presigned URL for the shared file, minted fresh per request. <c>disposition</c>
    /// selects <c>attachment</c> (default, download) or <c>inline</c> (preview) — inline is refused
    /// for anything off the preview allowlist, exactly as the owner path is.
    /// </summary>
    [HttpGet("{token}/download-url")]
    [ProducesResponseType<ShareDownloadUrlResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> DownloadUrl(
        string token, [FromQuery] string? disposition, CancellationToken ct)
    {
        var result = await shares.ResolveAsync(token, ct);
        if (result.Status == ShareResolveStatus.NotFound) return LinkNotFound();
        if (result.Status == ShareResolveStatus.Gone) return LinkUnavailable();

        var file = result.File!;
        var inline = string.Equals(disposition, "inline", StringComparison.OrdinalIgnoreCase);

        if (!inline)
            return Ok(new ShareDownloadUrlResponse(
                await storage.PresignGetUrlAsync(
                    file.StorageKey, PresignHeaders.ForDownload(file.OriginalName), ct),
                PresignExpiry));

        var serveAs = PreviewPolicy.ServeAs(file.ContentType);
        if (serveAs is null)
            return Problem("This file type cannot be previewed; download it instead.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);

        return Ok(new ShareDownloadUrlResponse(
            await storage.PresignGetUrlAsync(
                file.StorageKey, PresignHeaders.ForInline(file.OriginalName, serveAs), ct),
            PresignExpiry));
    }

    /// <summary>
    /// 404 for an unknown token. The public viewer has no context of its own, so — unlike the
    /// owner API, which returns a bare NotFound for a missing resource — these carry a problem+json
    /// message the page can render directly.
    /// </summary>
    private IActionResult LinkNotFound() =>
        Problem("This share link doesn't exist.", statusCode: StatusCodes.Status404NotFound);

    /// <summary>
    /// 410 Gone: the token was valid entropy but the link no longer works — expired, revoked, its
    /// file was removed, or sharing is disabled. The message stays generic across those causes so
    /// it never claims a reason that isn't the real one. Distinct from 404 (never existed), which
    /// the unguessable token makes safe to expose.
    /// </summary>
    private IActionResult LinkUnavailable() =>
        Problem("This share link is no longer available.", statusCode: StatusCodes.Status410Gone);
}
