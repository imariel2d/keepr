using Keepr.Api.Data;
using Keepr.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Keepr.Api.Services;

/// <summary>
/// A folder operation the caller can fix. <see cref="StatusCode"/> carries the HTTP meaning so
/// controllers don't have to pattern-match on the message.
/// </summary>
public class FolderException(string message, int statusCode = StatusCodes.Status400BadRequest)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// Owner-scoped folder tree operations.
///
/// The hierarchy is a pure adjacency list — <see cref="Folder.ParentId"/> is the only
/// representation — so a move is a one-row update and nothing can drift out of sync. Ancestor
/// and subtree walks use recursive CTEs against the (OwnerId, ParentId) index; they run on the
/// write path and on breadcrumb rendering, never on the hot "list this folder" query.
/// See docs/folder-hierarchy-design.md (Q-H).
/// </summary>
public class FolderService(AppDbContext db)
{
    /// <summary>
    /// Guards against runaway nesting (a looping client), not against people — real trees are
    /// under ~10 deep. Freely changeable: nothing in the schema depends on it.
    /// </summary>
    public const int MaxDepth = 32;

    /// <summary>
    /// Schema-qualified table name for the raw CTEs below. Hand-written SQL does not pick up the
    /// model's default schema, so it has to be spelled out — same reasoning as QuotaService.
    /// </summary>
    private const string FoldersTable = $"{AppDbContext.Schema}.\"Folders\"";

    // ---- Reads ---------------------------------------------------------------

    /// <summary>The folder if it exists, is live, and belongs to the caller; otherwise null.</summary>
    public Task<Folder?> OwnedAsync(Guid ownerId, Guid id, CancellationToken ct) =>
        db.Folders.SingleOrDefaultAsync(f => f.Id == id && f.OwnerId == ownerId, ct);

    /// <summary>Ancestors of <paramref name="id"/>, root first, including the folder itself.</summary>
    public async Task<List<Folder>> BreadcrumbsAsync(Guid ownerId, Guid id, CancellationToken ct)
    {
        var ordered = await AncestorIdsAsync(ownerId, id, ct);
        if (ordered.Count == 0) return [];

        var byId = await db.Folders
            .Where(f => ordered.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, ct);

        // Reorder in memory: the CTE returns root-first, a WHERE ... IN does not preserve that.
        return ordered.Where(byId.ContainsKey).Select(i => byId[i]).ToList();
    }

    /// <summary>Every live folder id in the subtree rooted at <paramref name="rootId"/>, inclusive.</summary>
    public Task<List<Guid>> SubtreeIdsAsync(Guid ownerId, Guid rootId, CancellationToken ct) =>
        db.Database.SqlQueryRaw<Guid>($"""
            WITH RECURSIVE sub AS (
                SELECT "Id" FROM {FoldersTable}
                 WHERE "Id" = @p0 AND "OwnerId" = @p1 AND "DeletedAt" IS NULL
                UNION ALL
                SELECT f."Id" FROM {FoldersTable} f
                  JOIN sub s ON f."ParentId" = s."Id"
                 WHERE f."OwnerId" = @p1 AND f."DeletedAt" IS NULL
            )
            SELECT "Id" AS "Value" FROM sub
            """, rootId, ownerId).ToListAsync(ct);

    /// <summary>1 for a root-level folder. 0 when the id is null (the root itself).</summary>
    public async Task<int> DepthAsync(Guid ownerId, Guid? id, CancellationToken ct)
    {
        if (id is null) return 0;
        var ids = await AncestorIdsAsync(ownerId, id.Value, ct);
        return ids.Count;
    }

    /// <summary>Levels contained in a subtree: 1 for a leaf, 2 for a folder with children.</summary>
    public Task<int> SubtreeHeightAsync(Guid ownerId, Guid rootId, CancellationToken ct) =>
        db.Database.SqlQueryRaw<int>($"""
            WITH RECURSIVE sub AS (
                SELECT "Id", 1 AS depth FROM {FoldersTable}
                 WHERE "Id" = @p0 AND "OwnerId" = @p1 AND "DeletedAt" IS NULL
                UNION ALL
                SELECT f."Id", s.depth + 1 FROM {FoldersTable} f
                  JOIN sub s ON f."ParentId" = s."Id"
                 WHERE f."OwnerId" = @p1 AND f."DeletedAt" IS NULL
            )
            SELECT COALESCE(MAX(depth), 0) AS "Value" FROM sub
            """, rootId, ownerId).SingleAsync(ct);

    /// <summary>Ancestor ids root-first, including the folder itself.</summary>
    private async Task<List<Guid>> AncestorIdsAsync(Guid ownerId, Guid id, CancellationToken ct)
    {
        var rows = await db.Database.SqlQueryRaw<Guid>($"""
            WITH RECURSIVE anc AS (
                SELECT "Id", "ParentId", 1 AS climb FROM {FoldersTable}
                 WHERE "Id" = @p0 AND "OwnerId" = @p1 AND "DeletedAt" IS NULL
                UNION ALL
                SELECT f."Id", f."ParentId", a.climb + 1 FROM {FoldersTable} f
                  JOIN anc a ON f."Id" = a."ParentId"
                 WHERE f."OwnerId" = @p1 AND f."DeletedAt" IS NULL
            )
            SELECT "Id" AS "Value" FROM anc ORDER BY climb DESC
            """, id, ownerId).ToListAsync(ct);
        return rows;
    }

    // ---- Writes --------------------------------------------------------------

    /// <summary>Create a folder, auto-suffixing the name if a sibling already has it.</summary>
    public Task<Folder> CreateAsync(Guid ownerId, string name, Guid? parentId, CancellationToken ct) =>
        NameConflict.RetryAsync(db, async () =>
        {
            var clean = ValidateName(name);

            if (parentId is not null)
            {
                var parent = await OwnedAsync(ownerId, parentId.Value, ct)
                    ?? throw new FolderException("Parent folder not found.", StatusCodes.Status404NotFound);
                if (await DepthAsync(ownerId, parent.Id, ct) + 1 > MaxDepth)
                    throw new FolderException($"Folder nesting is limited to {MaxDepth} levels.", StatusCodes.Status409Conflict);
            }

            var folder = new Folder { OwnerId = ownerId, ParentId = parentId };
            folder.SetName(await FreeNameAsync(ownerId, parentId, clean, ct));

            db.Folders.Add(folder);
            await db.SaveChangesAsync(ct);
            return folder;
        });

    /// <summary>Rename in place. One row, two columns — descendants are untouched.</summary>
    public Task<Folder> RenameAsync(Guid ownerId, Guid id, string name, CancellationToken ct) =>
        NameConflict.RetryAsync(db, async () =>
        {
            var clean = ValidateName(name);
            var folder = await OwnedAsync(ownerId, id, ct)
                ?? throw new FolderException("Folder not found.", StatusCodes.Status404NotFound);

            if (!string.Equals(folder.Name, clean, StringComparison.Ordinal))
            {
                folder.SetName(await FreeNameAsync(ownerId, folder.ParentId, clean, ct, excludeId: id));
                folder.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            return folder;
        });

    /// <summary>
    /// Re-parent a folder. This is a single row update: descendants follow automatically because
    /// their own ParentId links are unchanged — the whole point of the adjacency list.
    /// </summary>
    public Task<Folder> MoveAsync(Guid ownerId, Guid id, Guid? newParentId, CancellationToken ct) =>
        NameConflict.RetryAsync(db, async () =>
        {
            var folder = await OwnedAsync(ownerId, id, ct)
                ?? throw new FolderException("Folder not found.", StatusCodes.Status404NotFound);
            if (folder.ParentId == newParentId) return folder;

            if (newParentId is not null)
            {
                if (newParentId == id)
                    throw new FolderException("A folder cannot be moved into itself.", StatusCodes.Status409Conflict);

                _ = await OwnedAsync(ownerId, newParentId.Value, ct)
                    ?? throw new FolderException("Destination folder not found.", StatusCodes.Status404NotFound);

                // Cycle check: the destination must not live inside the subtree being moved,
                // or the branch would be spliced out of the tree entirely.
                var subtree = await SubtreeIdsAsync(ownerId, id, ct);
                if (subtree.Contains(newParentId.Value))
                    throw new FolderException("A folder cannot be moved into one of its own subfolders.", StatusCodes.Status409Conflict);

                var height = await SubtreeHeightAsync(ownerId, id, ct);
                if (await DepthAsync(ownerId, newParentId, ct) + height > MaxDepth)
                    throw new FolderException($"That move would nest folders deeper than {MaxDepth} levels.", StatusCodes.Status409Conflict);
            }

            folder.ParentId = newParentId;
            folder.SetName(await FreeNameAsync(ownerId, newParentId, folder.Name, ct, excludeId: id));
            folder.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return folder;
        });

    // ---- Helpers -------------------------------------------------------------

    /// <summary>First free sibling name in <paramref name="parentId"/>, suffixing if needed.</summary>
    private async Task<string> FreeNameAsync(
        Guid ownerId, Guid? parentId, string desired, CancellationToken ct, Guid? excludeId = null)
    {
        var pattern = NameAllocator.SeriesPattern(desired, isFile: false);
        var taken = await db.Folders
            .Where(f => f.OwnerId == ownerId && f.ParentId == parentId && f.Id != excludeId
                        && EF.Functions.Like(f.NameLower, pattern, "\\"))
            .Select(f => f.NameLower)
            .ToListAsync(ct);

        return NameAllocator.Allocate(desired, taken.ToHashSet(), isFile: false);
    }

    /// <summary>Trims and rejects names that would be confusing or unusable in a path-like UI.</summary>
    public static string ValidateName(string name)
    {
        var clean = name?.Trim() ?? string.Empty;
        if (clean.Length == 0) throw new FolderException("Name is required.");
        if (clean.Length > 255) throw new FolderException("Name is limited to 255 characters.");
        if (clean is "." or "..") throw new FolderException("Name cannot be \".\" or \"..\".");
        if (clean.Any(c => c is '/' or '\\' || char.IsControl(c)))
            throw new FolderException("Name cannot contain slashes or control characters.");
        return clean;
    }
}
