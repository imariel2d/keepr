using Keepr.Api.Data;
using Keepr.Api.Domain;
using Keepr.Api.Services;
using Keepr.Api.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Keepr.Api.Features.Media;

/// <summary>
/// <paramref name="PreviewKind"/> is "image", "pdf", "video", "audio", or null when the file
/// can only be downloaded. Computed server-side from an allowlist so the client never has to
/// decide what is safe to render — see <see cref="PreviewPolicy"/>.
/// </summary>
public record MediaListItem(
    Guid Id, string OriginalName, string? ContentType, long SizeBytes, Guid? FolderId,
    DateTimeOffset CreatedAt, string? PreviewKind);
/// <summary>
/// A presigned URL and the moment it stops working. <see cref="ExpiresAt"/> exists so clients
/// can cache the URL without having to parse the AWS signature's own expiry fields.
/// </summary>
public record DownloadUrlResponse(string Url, DateTimeOffset ExpiresAt);
public record RenameMediaRequest(string OriginalName);

/// <summary>Destination for a move. A null <see cref="FolderId"/> means the owner's root.</summary>
public record MoveMediaRequest(Guid? FolderId);

/// <summary>Owner-scoped listing, presigned download, rename/move, and delete-to-trash.</summary>
[ApiController]
[Authorize]
[Route("api/media")]
public class MediaController(
    AppDbContext db,
    IObjectStorage storage,
    TrashService trash,
    IOptions<StorageOptions> storageOptions) : ControllerBase
{
    private DateTimeOffset PresignExpiry =>
        DateTimeOffset.UtcNow.AddMinutes(storageOptions.Value.PresignExpiryMinutes);

    /// <summary>
    /// The owner's files. Pass <paramref name="folderId"/> to scope to one folder; omit it for a
    /// flat list of everything, which is what the "all files" and search views want.
    /// Trashed files are excluded automatically by the soft-delete query filter.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<MediaListItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MediaListItem>>> List(
        [FromQuery] Guid? folderId, [FromQuery] bool scoped, CancellationToken ct)
    {
        var userId = User.UserId();
        var query = db.MediaFiles.Where(m => m.OwnerId == userId && m.Status == MediaStatus.Ready);

        // `scoped` distinguishes "files at the root" (scoped=true, folderId omitted) from
        // "every file anywhere" (scoped=false) — both of which send no folderId.
        if (scoped) query = query.Where(m => m.FolderId == folderId);

        // Projected after materialising: the allowlist is a dictionary lookup, not something
        // EF can translate to SQL.
        var rows = await query
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new { m.Id, m.OriginalName, m.ContentType, m.SizeBytes, m.FolderId, m.CreatedAt })
            .ToListAsync(ct);

        var items = rows.Select(m => new MediaListItem(
            m.Id, m.OriginalName, m.ContentType, m.SizeBytes, m.FolderId, m.CreatedAt,
            PreviewPolicy.KindName(m.ContentType))).ToList();
        return Ok(items);
    }

    /// <summary>
    /// Short-TTL presigned GET. <paramref name="disposition"/> selects what the browser does
    /// with it:
    /// <list type="bullet">
    /// <item><c>attachment</c> (default) — saves the file under its real name. Without this the
    /// browser falls back to the URL path, which is an opaque UUID.</item>
    /// <item><c>inline</c> — renders it in the page, for preview. Allowed only for types on the
    /// preview allowlist, and served as the allowlist's content type rather than the stored one.</item>
    /// </list>
    /// </summary>
    [HttpGet("{id:guid}/download-url")]
    [ProducesResponseType<DownloadUrlResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult<DownloadUrlResponse>> DownloadUrl(
        Guid id, [FromQuery] string? disposition, CancellationToken ct)
    {
        var media = await OwnedReady(id, ct);
        if (media is null) return NotFound();

        var inline = string.Equals(disposition, "inline", StringComparison.OrdinalIgnoreCase);
        if (!inline)
            return new DownloadUrlResponse(
                await storage.PresignGetUrlAsync(
                    media.StorageKey, PresignHeaders.ForDownload(media.OriginalName), ct),
                PresignExpiry);

        // Defence in depth: the client uses the same allowlist to pick a render element, but the
        // server refuses to mint an inline URL for anything not on it.
        var serveAs = PreviewPolicy.ServeAs(media.ContentType);
        if (serveAs is null)
            return Problem(
                "This file type cannot be previewed; download it instead.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);

        return new DownloadUrlResponse(
            await storage.PresignGetUrlAsync(
                media.StorageKey, PresignHeaders.ForInline(media.OriginalName, serveAs), ct),
            PresignExpiry);
    }

    /// <summary>
    /// Rename. The stored name may differ from the requested one: a collision inside the folder
    /// is resolved by suffixing ("report.pdf" → "report (2).pdf"), so use the response value.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<MediaListItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MediaListItem>> Rename(
        Guid id, RenameMediaRequest req, CancellationToken ct)
    {
        var media = await OwnedReady(id, ct);
        if (media is null) return NotFound();

        string clean;
        try
        {
            clean = FolderService.ValidateName(req.OriginalName);
        }
        catch (FolderException ex)
        {
            return Problem(ex.Message, statusCode: ex.StatusCode);
        }

        return await NameConflict.RetryAsync(db, async () =>
        {
            var target = await OwnedReady(id, ct);
            if (target is null) return (ActionResult<MediaListItem>)NotFound();

            target.SetOriginalName(await FreeNameAsync(target.OwnerId, target.FolderId, clean, id, ct));
            target.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return ToItem(target);
        });
    }

    /// <summary>Move a file to another folder, or to the root with a null folderId.</summary>
    [HttpPost("{id:guid}/move")]
    [ProducesResponseType<MediaListItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MediaListItem>> Move(Guid id, MoveMediaRequest req, CancellationToken ct)
    {
        var userId = User.UserId();
        var media = await OwnedReady(id, ct);
        if (media is null) return NotFound();

        if (req.FolderId is not null)
        {
            var exists = await db.Folders.AnyAsync(
                f => f.Id == req.FolderId && f.OwnerId == userId, ct);
            if (!exists) return Problem("Destination folder not found.", statusCode: StatusCodes.Status404NotFound);
        }

        return await NameConflict.RetryAsync(db, async () =>
        {
            var target = await OwnedReady(id, ct);
            if (target is null) return (ActionResult<MediaListItem>)NotFound();

            target.FolderId = req.FolderId;
            target.SetOriginalName(await FreeNameAsync(userId, req.FolderId, target.OriginalName, id, ct));
            target.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return ToItem(target);
        });
    }

    /// <summary>
    /// Move to trash. Instant and reversible — no object-storage call happens here; the bytes are
    /// freed when the retention window expires (or on an explicit purge).
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            await trash.TrashFileAsync(User.UserId(), id, ct);
            return NoContent();
        }
        catch (TrashException ex)
        {
            return Problem(ex.Message, statusCode: ex.StatusCode);
        }
    }

    private async Task<string> FreeNameAsync(
        Guid ownerId, Guid? folderId, string desired, Guid excludeId, CancellationToken ct)
    {
        var pattern = NameAllocator.SeriesPattern(desired, isFile: true);
        var taken = await db.MediaFiles
            .Where(m => m.OwnerId == ownerId && m.FolderId == folderId && m.Id != excludeId
                        && m.Status != MediaStatus.Failed
                        && EF.Functions.Like(m.OriginalNameLower, pattern, "\\"))
            .Select(m => m.OriginalNameLower)
            .ToListAsync(ct);
        return NameAllocator.Allocate(desired, taken.ToHashSet(), isFile: true);
    }

    private static MediaListItem ToItem(MediaFile m) =>
        new(m.Id, m.OriginalName, m.ContentType, m.SizeBytes, m.FolderId, m.CreatedAt,
            PreviewPolicy.KindName(m.ContentType));

    private async Task<MediaFile?> OwnedReady(Guid id, CancellationToken ct)
    {
        var userId = User.UserId();
        return await db.MediaFiles.SingleOrDefaultAsync(
            m => m.Id == id && m.OwnerId == userId && m.Status == MediaStatus.Ready, ct);
    }
}
