using Keepr.Api.Data;
using Keepr.Api.Domain;
using Keepr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Keepr.Api.Features.Sharing;

/// <param name="ExpiresInDays">How long the link stays live. Below 1 is rejected; above the
/// configured maximum is capped, not refused.</param>
public record CreateShareRequest(int ExpiresInDays);
public record UpdateShareRequest(int ExpiresInDays);

/// <summary>The link URL, returned only at creation — the token is never stored, so this is the
/// one chance to copy it. See docs/shareable-links-design.md §3.1.</summary>
public record CreatedShareResponse(Guid LinkId, string Url, DateTimeOffset ExpiresAt);

/// <summary>A link for management. No URL: it cannot be reconstructed from the stored digest.</summary>
public record ShareLinkResponse(
    Guid LinkId, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, bool Revoked,
    DateTimeOffset? LastAccessedAt);

public record StopSharingResponse(int Revoked);

/// <summary>
/// Owner-side management of "anyone with the link" shares: mint, list, edit expiry, and stop
/// sharing (one link or a whole file). The public read path lives in <see cref="PublicShareController"/>.
/// </summary>
[ApiController]
[Authorize]
public class SharesController(AppDbContext db, ShareLinkService shares) : ControllerBase
{
    /// <summary>Create a link for a file you own. The URL in the response is shown only once.</summary>
    [HttpPost("api/media/{id:guid}/share")]
    [ProducesResponseType<CreatedShareResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(Guid id, CreateShareRequest req, CancellationToken ct)
    {
        if (req.ExpiresInDays < 1)
            return Problem("A link must be valid for at least one day.",
                statusCode: StatusCodes.Status400BadRequest);

        var userId = User.UserId();
        if (!await OwnsReadyFile(id, userId, ct)) return NotFound();

        var (link, token) = await shares.IssueAsync(id, userId, req.ExpiresInDays, ct);
        return Ok(new CreatedShareResponse(link.Id, shares.BuildUrl(token), link.ExpiresAt));
    }

    /// <summary>List the links on a file you own, newest first. Never returns the URL.</summary>
    [HttpGet("api/media/{id:guid}/shares")]
    [ProducesResponseType<IReadOnlyList<ShareLinkResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid id, CancellationToken ct)
    {
        var userId = User.UserId();
        if (!await OwnsReadyFile(id, userId, ct)) return NotFound();

        var links = await shares.ListForFileAsync(id, userId, ct);
        return Ok(links.Select(ToResponse).ToList());
    }

    /// <summary>Stop sharing the file — revoke every live link on it at once.</summary>
    [HttpDelete("api/media/{id:guid}/shares")]
    [ProducesResponseType<StopSharingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StopSharingFile(Guid id, CancellationToken ct)
    {
        var userId = User.UserId();
        if (!await OwnsReadyFile(id, userId, ct)) return NotFound();

        var revoked = await shares.RevokeAllForFileAsync(id, userId, ct);
        return Ok(new StopSharingResponse(revoked));
    }

    /// <summary>Change a link's expiry. Rejected on a revoked link — revocation is terminal.</summary>
    [HttpPatch("api/shares/{linkId:guid}")]
    [ProducesResponseType<ShareLinkResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateExpiry(Guid linkId, UpdateShareRequest req, CancellationToken ct)
    {
        if (req.ExpiresInDays < 1)
            return Problem("A link must be valid for at least one day.",
                statusCode: StatusCodes.Status400BadRequest);

        var (status, link) = await shares.UpdateExpiryAsync(linkId, User.UserId(), req.ExpiresInDays, ct);
        return status switch
        {
            UpdateExpiryStatus.NotFound => NotFound(),
            UpdateExpiryStatus.Revoked => Problem(
                "This link has been revoked; create a new one to share again.",
                statusCode: StatusCodes.Status409Conflict),
            _ => Ok(ToResponse(link!))
        };
    }

    /// <summary>Stop sharing one link. Idempotent.</summary>
    [HttpDelete("api/shares/{linkId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(Guid linkId, CancellationToken ct) =>
        await shares.RevokeAsync(linkId, User.UserId(), ct) ? NoContent() : NotFound();

    private async Task<bool> OwnsReadyFile(Guid fileId, Guid userId, CancellationToken ct) =>
        await db.MediaFiles.AnyAsync(
            m => m.Id == fileId && m.OwnerId == userId && m.Status == MediaStatus.Ready, ct);

    private static ShareLinkResponse ToResponse(ShareLink s) =>
        new(s.Id, s.CreatedAt, s.ExpiresAt, s.RevokedAt is not null, s.LastAccessedAt);
}
