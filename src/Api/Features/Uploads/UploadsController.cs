using Keepr.Api.Data;
using Keepr.Api.Domain;
using Keepr.Api.Services;
using Keepr.Api.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Keepr.Api.Features.Uploads;

// ---- DTOs ------------------------------------------------------------------
public record InitUploadRequest(string OriginalName, long SizeBytes, string? ContentType, Guid? FolderId);

/// <summary>
/// <see cref="OriginalName"/> is the name actually stored, which may differ from the requested
/// one when a file of that name already exists in the destination folder — the collision is
/// resolved by suffixing rather than by failing the upload. Display this, not the local filename.
/// </summary>
public record InitUploadResponse(
    Guid MediaId, string Key, string UploadId, int PartSize, string OriginalName, Guid? FolderId);
public record PartUrlResponse(int PartNumber, string Url);
public record CompletePart(int PartNumber, string ETag);
public record CompleteUploadRequest(IReadOnlyList<CompletePart> Parts);

/// <summary>
/// Presigned S3 multipart upload flow with quota accounting.
/// See docs/ai-design-decisions.md (D2, D9).
/// </summary>
[ApiController]
[Authorize]
[Route("api/uploads")]
public class UploadsController(
    AppDbContext db,
    IObjectStorage storage,
    QuotaService quota) : ControllerBase
{
    // 16 MB parts. R2/S3 require all-but-last part >= 5 MB.
    private const int PartSize = 16 * 1024 * 1024;

    /// <summary>Step 1: reserve quota, open a multipart upload, create a pending row.</summary>
    [HttpPost("init")]
    public async Task<ActionResult<InitUploadResponse>> Init(InitUploadRequest req, CancellationToken ct)
    {
        if (req.SizeBytes <= 0) return BadRequest(new { error = "SizeBytes must be positive." });

        var userId = User.UserId();

        string desiredName;
        try
        {
            desiredName = FolderService.ValidateName(req.OriginalName);
        }
        catch (FolderException ex)
        {
            return Problem(ex.Message, statusCode: ex.StatusCode);
        }

        if (req.FolderId is not null &&
            !await db.Folders.AnyAsync(f => f.Id == req.FolderId && f.OwnerId == userId, ct))
            return Problem("Destination folder not found.", statusCode: StatusCodes.Status404NotFound);

        var ext = Path.GetExtension(desiredName);
        var key = $"{userId}/{Guid.NewGuid():N}{ext}";

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await quota.ReserveAsync(userId, req.SizeBytes, ct);

            var uploadId = await storage.CreateMultipartUploadAsync(key, req.ContentType, ct);

            var media = new MediaFile
            {
                OwnerId = userId,
                FolderId = req.FolderId,
                StorageKey = key,
                ContentType = req.ContentType,
                SizeBytes = req.SizeBytes,
                MultipartUploadId = uploadId,
                Status = MediaStatus.Pending
            };
            // Claim the name now, while Pending: the uniqueness index covers pending rows too, so
            // a collision surfaces here rather than after the user has waited out a whole upload.
            media.SetOriginalName(await FreeNameAsync(userId, req.FolderId, desiredName, ct));

            db.MediaFiles.Add(media);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new InitUploadResponse(
                media.Id, key, uploadId, PartSize, media.OriginalName, media.FolderId);
        }
        catch (QuotaExceededException ex)
        {
            await tx.RollbackAsync(ct);
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new { error = ex.Message, ex.Remaining });
        }
    }

    private async Task<string> FreeNameAsync(
        Guid ownerId, Guid? folderId, string desired, CancellationToken ct)
    {
        var pattern = NameAllocator.SeriesPattern(desired, isFile: true);
        var taken = await db.MediaFiles
            .Where(m => m.OwnerId == ownerId && m.FolderId == folderId
                        && m.Status != MediaStatus.Failed
                        && EF.Functions.Like(m.OriginalNameLower, pattern, "\\"))
            .Select(m => m.OriginalNameLower)
            .ToListAsync(ct);
        return NameAllocator.Allocate(desired, taken.ToHashSet(), isFile: true);
    }

    /// <summary>Step 2: presigned URL for one part. Client calls this per part (or batches).</summary>
    [HttpGet("{id:guid}/part-url")]
    public async Task<ActionResult<PartUrlResponse>> PartUrl(Guid id, [FromQuery] int partNumber, CancellationToken ct)
    {
        if (partNumber < 1) return BadRequest(new { error = "partNumber is 1-based." });
        var media = await OwnedPending(id, ct);
        if (media is null) return NotFound();

        var url = await storage.PresignUploadPartUrlAsync(media.StorageKey, media.MultipartUploadId!, partNumber, ct);
        return new PartUrlResponse(partNumber, url);
    }

    /// <summary>Step 3: assemble parts, verify real size, reconcile quota, mark ready.</summary>
    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, CompleteUploadRequest req, CancellationToken ct)
    {
        var media = await OwnedPending(id, ct);
        if (media is null) return NotFound();
        if (req.Parts is null || req.Parts.Count == 0)
            return BadRequest(new { error = "No parts supplied." });

        var actualSize = await storage.CompleteMultipartUploadAsync(
            media.StorageKey,
            media.MultipartUploadId!,
            req.Parts.Select(p => new CompletedPart(p.PartNumber, p.ETag)).ToList(),
            ct);

        // Trust the bytes, not the client: detect the real content-type from magic bytes (D6).
        // Fall back to the client's declared type when the signature is unrecognized.
        var head = await storage.ReadHeadBytesAsync(media.StorageKey, ContentTypeSniffer.BytesNeeded, ct);
        media.ContentType = ContentTypeSniffer.Detect(head) ?? media.ContentType;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await quota.ReconcileAsync(media.OwnerId, media.SizeBytes, actualSize, ct);
        media.SizeBytes = actualSize;
        media.MultipartUploadId = null;
        media.Status = MediaStatus.Ready;
        media.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Ok(new { media.Id, media.SizeBytes, media.ContentType, status = media.Status.ToString() });
    }

    /// <summary>Abort an in-flight upload; release the reserved quota.</summary>
    [HttpPost("{id:guid}/abort")]
    public async Task<IActionResult> Abort(Guid id, CancellationToken ct)
    {
        var media = await OwnedPending(id, ct);
        if (media is null) return NotFound();

        await storage.AbortMultipartUploadAsync(media.StorageKey, media.MultipartUploadId!, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await quota.ReleaseAsync(media.OwnerId, media.SizeBytes, ct);
        media.Status = MediaStatus.Failed;
        media.MultipartUploadId = null;
        media.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return NoContent();
    }

    private async Task<MediaFile?> OwnedPending(Guid id, CancellationToken ct)
    {
        var userId = User.UserId();
        return await db.MediaFiles.SingleOrDefaultAsync(
            m => m.Id == id && m.OwnerId == userId && m.Status == MediaStatus.Pending, ct);
    }
}
