using Keepr.Api.Data;
using Keepr.Api.Domain;
using Keepr.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace Keepr.Api.Services;

/// <summary>A trash operation the caller can fix; <see cref="StatusCode"/> carries the HTTP meaning.</summary>
public class TrashException(string message, int statusCode = StatusCodes.Status400BadRequest)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>What a trashed entry is, for the unified trash listing.</summary>
public enum TrashKind { File, Folder }

public record TrashEntry(
    Guid Id, TrashKind Kind, string Name, long SizeBytes,
    DateTimeOffset DeletedAt, DateTimeOffset PurgesAt);

/// <summary>
/// Soft delete: deleting stamps <c>DeletedAt</c> and is fully transactional with no object-storage
/// calls, so it is instant and reversible. Bytes are freed only when the retention window expires
/// and <see cref="TrashPurgeService"/> purges. See docs/trash-soft-delete-design.md.
/// </summary>
public class TrashService(
    AppDbContext db,
    FolderService folders,
    IObjectStorage storage,
    QuotaService quota)
{
    private const string FoldersTable = $"{AppDbContext.Schema}.\"Folders\"";
    private const string FilesTable = $"{AppDbContext.Schema}.\"MediaFiles\"";

    // ---- Trash ---------------------------------------------------------------

    /// <summary>
    /// Trash a folder and everything under it, in one transaction. Rows that were already trashed
    /// keep their original <c>DeletedRootId</c> and their original clock — so restoring this
    /// folder later will not resurrect a subfolder the user threw away separately.
    /// </summary>
    public async Task TrashFolderAsync(Guid ownerId, Guid folderId, CancellationToken ct)
    {
        _ = await folders.OwnedAsync(ownerId, folderId, ct)
            ?? throw new TrashException("Folder not found.", StatusCodes.Status404NotFound);

        var subtree = await folders.SubtreeIdsAsync(ownerId, folderId, ct);
        var now = DateTimeOffset.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.Database.ExecuteSqlRawAsync($"""
            UPDATE {FilesTable}
               SET "DeletedAt" = @p0, "DeletedRootId" = @p1, "UpdatedAt" = @p0
             WHERE "OwnerId" = @p2 AND "FolderId" = ANY(@p3) AND "DeletedAt" IS NULL
            """, [now, folderId, ownerId, subtree.ToArray()], ct);

        await db.Database.ExecuteSqlRawAsync($"""
            UPDATE {FoldersTable}
               SET "DeletedAt" = @p0, "DeletedRootId" = @p1, "UpdatedAt" = @p0
             WHERE "OwnerId" = @p2 AND "Id" = ANY(@p3) AND "DeletedAt" IS NULL
            """, [now, folderId, ownerId, subtree.ToArray()], ct);

        await tx.CommitAsync(ct);
    }

    /// <summary>Trash a single file. No object-storage call — the bytes stay until purge.</summary>
    public async Task TrashFileAsync(Guid ownerId, Guid mediaId, CancellationToken ct)
    {
        var media = await db.MediaFiles.SingleOrDefaultAsync(
            m => m.Id == mediaId && m.OwnerId == ownerId && m.Status == MediaStatus.Ready, ct)
            ?? throw new TrashException("File not found.", StatusCodes.Status404NotFound);

        media.DeletedAt = DateTimeOffset.UtcNow;
        media.DeletedRootId = media.Id;
        media.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    // ---- List ----------------------------------------------------------------

    /// <summary>
    /// Trash contents: only the items the user deleted directly (<c>DeletedRootId == Id</c>), so
    /// trashing a folder of 400 files shows one entry, not 401.
    /// </summary>
    public async Task<List<TrashEntry>> ListAsync(Guid ownerId, int retentionDays, CancellationToken ct)
    {
        var files = await db.MediaFiles.IgnoreQueryFilters()
            .Where(m => m.OwnerId == ownerId && m.DeletedAt != null && m.DeletedRootId == m.Id)
            .Select(m => new { m.Id, m.OriginalName, m.SizeBytes, m.DeletedAt })
            .ToListAsync(ct);

        var trashedFolders = await db.Folders.IgnoreQueryFilters()
            .Where(f => f.OwnerId == ownerId && f.DeletedAt != null && f.DeletedRootId == f.Id)
            .Select(f => new { f.Id, f.Name, f.DeletedAt })
            .ToListAsync(ct);

        // Bytes held by each trashed folder, so the UI can explain what "Empty Trash" would free.
        var folderIds = trashedFolders.Select(f => f.Id).ToList();
        var folderSizes = await db.MediaFiles.IgnoreQueryFilters()
            .Where(m => m.OwnerId == ownerId && m.DeletedRootId != null
                        && folderIds.Contains(m.DeletedRootId.Value))
            .GroupBy(m => m.DeletedRootId!.Value)
            .Select(g => new { RootId = g.Key, Bytes = g.Sum(m => m.SizeBytes) })
            .ToDictionaryAsync(x => x.RootId, x => x.Bytes, ct);

        var entries = files
            .Select(f => new TrashEntry(
                f.Id, TrashKind.File, f.OriginalName, f.SizeBytes,
                f.DeletedAt!.Value, f.DeletedAt!.Value.AddDays(retentionDays)))
            .Concat(trashedFolders.Select(f => new TrashEntry(
                f.Id, TrashKind.Folder, f.Name, folderSizes.GetValueOrDefault(f.Id),
                f.DeletedAt!.Value, f.DeletedAt!.Value.AddDays(retentionDays))));

        return entries.OrderByDescending(e => e.DeletedAt).ToList();
    }

    /// <summary>Bytes currently sitting in the trash — still counted against the user's quota.</summary>
    public Task<long> TrashedBytesAsync(Guid ownerId, CancellationToken ct) =>
        db.MediaFiles.IgnoreQueryFilters()
            .Where(m => m.OwnerId == ownerId && m.DeletedAt != null && m.Status == MediaStatus.Ready)
            .SumAsync(m => m.SizeBytes, ct);

    // ---- Restore -------------------------------------------------------------

    /// <summary>
    /// Restore an item and everything trashed along with it. Two boundary conditions:
    /// the name may have been taken while it was away (it gets suffixed), and its parent may be
    /// gone or still trashed (it returns to the root rather than failing).
    /// </summary>
    public async Task<string> RestoreAsync(Guid ownerId, Guid id, CancellationToken ct) =>
        await NameConflict.RetryAsync(db, async () =>
        {
            var folder = await db.Folders.IgnoreQueryFilters()
                .SingleOrDefaultAsync(f => f.Id == id && f.OwnerId == ownerId && f.DeletedAt != null, ct);

            return folder is not null
                ? await RestoreFolderAsync(ownerId, folder, ct)
                : await RestoreFileAsync(ownerId, id, ct);
        });

    private async Task<string> RestoreFolderAsync(Guid ownerId, Folder folder, CancellationToken ct)
    {
        if (folder.DeletedRootId != folder.Id)
            throw new TrashException("This folder was deleted with its parent; restore the parent instead.", StatusCodes.Status409Conflict);

        // A parent that was purged, or is itself still trashed, cannot receive the restore.
        var parentId = await LiveFolderIdOrNullAsync(ownerId, folder.ParentId, ct);
        var name = await FreeFolderNameAsync(ownerId, parentId, folder.Name, folder.Id, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        folder.ParentId = parentId;
        folder.SetName(name);
        await db.SaveChangesAsync(ct);

        await ClearDeletionAsync(folder.Id, ct);
        await tx.CommitAsync(ct);
        return name;
    }

    private async Task<string> RestoreFileAsync(Guid ownerId, Guid id, CancellationToken ct)
    {
        var media = await db.MediaFiles.IgnoreQueryFilters()
            .SingleOrDefaultAsync(m => m.Id == id && m.OwnerId == ownerId && m.DeletedAt != null, ct)
            ?? throw new TrashException("Item not found in trash.", StatusCodes.Status404NotFound);

        if (media.DeletedRootId != media.Id)
            throw new TrashException("This file was deleted with its folder; restore the folder instead.", StatusCodes.Status409Conflict);

        var folderId = await LiveFolderIdOrNullAsync(ownerId, media.FolderId, ct);
        media.FolderId = folderId;
        media.SetOriginalName(await FreeFileNameAsync(ownerId, folderId, media.OriginalName, media.Id, ct));
        media.DeletedAt = null;
        media.DeletedRootId = null;
        media.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return media.OriginalName;
    }

    /// <summary>Revive everything that went to the trash as part of <paramref name="rootId"/>.</summary>
    private async Task ClearDeletionAsync(Guid rootId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        await db.Database.ExecuteSqlRawAsync($"""
            UPDATE {FilesTable}
               SET "DeletedAt" = NULL, "DeletedRootId" = NULL, "UpdatedAt" = @p0
             WHERE "DeletedRootId" = @p1
            """, [now, rootId], ct);

        await db.Database.ExecuteSqlRawAsync($"""
            UPDATE {FoldersTable}
               SET "DeletedAt" = NULL, "DeletedRootId" = NULL, "UpdatedAt" = @p0
             WHERE "DeletedRootId" = @p1
            """, [now, rootId], ct);
    }

    private async Task<Guid?> LiveFolderIdOrNullAsync(Guid ownerId, Guid? folderId, CancellationToken ct)
    {
        if (folderId is null) return null;
        var live = await folders.OwnedAsync(ownerId, folderId.Value, ct);
        return live?.Id;
    }

    // ---- Purge ---------------------------------------------------------------

    /// <summary>
    /// Permanently delete a trashed item now, skipping the retention wait. Objects go first and
    /// rows second: a crash in between leaves a visible row whose object is gone (retryable),
    /// where the reverse would leave an object nothing references (billed forever, invisible).
    /// </summary>
    public async Task PurgeAsync(Guid ownerId, Guid id, CancellationToken ct)
    {
        var folder = await db.Folders.IgnoreQueryFilters()
            .SingleOrDefaultAsync(f => f.Id == id && f.OwnerId == ownerId && f.DeletedAt != null, ct);

        if (folder is not null)
        {
            await PurgeGroupAsync(ownerId, id, ct);
            return;
        }

        var media = await db.MediaFiles.IgnoreQueryFilters()
            .SingleOrDefaultAsync(m => m.Id == id && m.OwnerId == ownerId && m.DeletedAt != null, ct)
            ?? throw new TrashException("Item not found in trash.", StatusCodes.Status404NotFound);

        await PurgeFilesAsync([media], ct);
    }

    /// <summary>Purge everything trashed as part of <paramref name="rootId"/>, files then folders.</summary>
    private async Task PurgeGroupAsync(Guid ownerId, Guid rootId, CancellationToken ct)
    {
        var files = await db.MediaFiles.IgnoreQueryFilters()
            .Where(m => m.OwnerId == ownerId && m.DeletedRootId == rootId)
            .ToListAsync(ct);

        await PurgeFilesAsync(files, ct);

        var folderIds = await db.Folders.IgnoreQueryFilters()
            .Where(f => f.OwnerId == ownerId && f.DeletedRootId == rootId)
            .Select(f => f.Id)
            .ToListAsync(ct);

        await PurgeFolderRowsAsync(folderIds, ct);
    }

    /// <summary>Delete objects, then release quota and delete rows in one transaction.</summary>
    public async Task PurgeFilesAsync(IReadOnlyList<MediaFile> files, CancellationToken ct)
    {
        if (files.Count == 0) return;

        await storage.DeleteManyAsync(files.Select(f => f.StorageKey).ToList(), ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var byOwner in files.GroupBy(f => f.OwnerId))
        {
            // Only Ready bytes were ever counted; Pending/Failed were released at abort.
            var bytes = byOwner.Where(f => f.Status == MediaStatus.Ready).Sum(f => f.SizeBytes);
            if (bytes > 0) await quota.ReleaseAsync(byOwner.Key, bytes, ct);
        }
        db.MediaFiles.RemoveRange(files);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Delete folder rows deepest-first. Rather than computing depth, this repeatedly deletes the
    /// folders that no longer have children — which satisfies the Restrict FK by construction and
    /// terminates in at most <see cref="FolderService.MaxDepth"/> passes.
    /// </summary>
    public async Task PurgeFolderRowsAsync(IReadOnlyList<Guid> folderIds, CancellationToken ct)
    {
        if (folderIds.Count == 0) return;
        var ids = folderIds.ToArray();

        for (var pass = 0; pass < FolderService.MaxDepth; pass++)
        {
            var removed = await db.Database.ExecuteSqlRawAsync($"""
                DELETE FROM {FoldersTable} f
                 WHERE f."Id" = ANY(@p0)
                   AND NOT EXISTS (SELECT 1 FROM {FoldersTable} c WHERE c."ParentId" = f."Id")
                   AND NOT EXISTS (SELECT 1 FROM {FilesTable} m WHERE m."FolderId" = f."Id")
                """, [ids], ct);

            if (removed == 0) break;
        }
    }

    // ---- Name helpers --------------------------------------------------------

    private async Task<string> FreeFolderNameAsync(
        Guid ownerId, Guid? parentId, string desired, Guid excludeId, CancellationToken ct)
    {
        var pattern = NameAllocator.SeriesPattern(desired, isFile: false);
        var taken = await db.Folders
            .Where(f => f.OwnerId == ownerId && f.ParentId == parentId && f.Id != excludeId
                        && EF.Functions.Like(f.NameLower, pattern, "\\"))
            .Select(f => f.NameLower)
            .ToListAsync(ct);
        return NameAllocator.Allocate(desired, taken.ToHashSet(), isFile: false);
    }

    private async Task<string> FreeFileNameAsync(
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
}
