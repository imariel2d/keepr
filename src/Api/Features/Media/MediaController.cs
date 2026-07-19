using Media.Api.Data;
using Media.Api.Domain;
using Media.Api.Services;
using Media.Api.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Media.Api.Features.Media;

public record MediaListItem(Guid Id, string OriginalName, string? ContentType, long SizeBytes, DateTimeOffset CreatedAt);
public record DownloadUrlResponse(string Url);

/// <summary>Owner-scoped listing, presigned download, and hard delete. See ai-design-decisions.md (D6, Q9).</summary>
[ApiController]
[Authorize]
[Route("api/media")]
public class MediaController(
    AppDbContext db,
    IObjectStorage storage,
    QuotaService quota) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MediaListItem>>> List(CancellationToken ct)
    {
        var userId = User.UserId();
        var items = await db.MediaFiles
            .Where(m => m.OwnerId == userId && m.Status == MediaStatus.Ready)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new MediaListItem(m.Id, m.OriginalName, m.ContentType, m.SizeBytes, m.CreatedAt))
            .ToListAsync(ct);
        return Ok(items);
    }

    /// <summary>Short-TTL presigned GET so the private object can be viewed/downloaded.</summary>
    [HttpGet("{id:guid}/download-url")]
    public async Task<ActionResult<DownloadUrlResponse>> DownloadUrl(Guid id, CancellationToken ct)
    {
        var media = await OwnedReady(id, ct);
        if (media is null) return NotFound();
        return new DownloadUrlResponse(await storage.PresignGetUrlAsync(media.StorageKey, ct));
    }

    /// <summary>Hard delete: remove the object, the row, and free the quota (Q9).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var media = await OwnedReady(id, ct);
        if (media is null) return NotFound();

        await storage.DeleteAsync(media.StorageKey, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await quota.ReleaseAsync(media.OwnerId, media.SizeBytes, ct);
        db.MediaFiles.Remove(media);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return NoContent();
    }

    private async Task<MediaFile?> OwnedReady(Guid id, CancellationToken ct)
    {
        var userId = User.UserId();
        return await db.MediaFiles.SingleOrDefaultAsync(
            m => m.Id == id && m.OwnerId == userId && m.Status == MediaStatus.Ready, ct);
    }
}
