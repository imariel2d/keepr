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
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<IActionResult> Resolve(string token, CancellationToken ct)
    {
        var result = await shares.ResolveAsync(token, ct);
        if (result.Status == ShareResolveStatus.NotFound) return NotFound();
        if (result.Status == ShareResolveStatus.Gone) return Gone();

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
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> DownloadUrl(
        string token, [FromQuery] string? disposition, CancellationToken ct)
    {
        var result = await shares.ResolveAsync(token, ct);
        if (result.Status == ShareResolveStatus.NotFound) return NotFound();
        if (result.Status == ShareResolveStatus.Gone) return Gone();

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

    /// <summary>410 Gone: the link was valid entropy but no longer works — expired, revoked, or its
    /// file was removed. Distinct from 404 (never existed), which the unguessable token makes safe
    /// to expose.</summary>
    private IActionResult Gone() => StatusCode(StatusCodes.Status410Gone);
}
