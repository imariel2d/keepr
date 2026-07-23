using Keepr.Api.Data;
using Keepr.Api.Domain;
using Keepr.Api.Features.Media;
using Keepr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Keepr.Api.Features.Folders;

// ---- DTOs ------------------------------------------------------------------
public record CreateFolderRequest(string Name, Guid? ParentId);
public record RenameFolderRequest(string Name);

/// <summary>Destination for a move. A null <see cref="ParentId"/> means the owner's root.</summary>
public record MoveFolderRequest(Guid? ParentId);

public record FolderItem(Guid Id, string Name, Guid? ParentId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public record Breadcrumb(Guid Id, string Name);

/// <summary>
/// One folder's contents. <see cref="Folder"/> and the last breadcrumb are null/empty at the
/// root, which has no row of its own (ParentId = null *is* the root).
/// </summary>
public record FolderContents(
    FolderItem? Folder,
    IReadOnlyList<Breadcrumb> Breadcrumbs,
    IReadOnlyList<FolderItem> Folders,
    IReadOnlyList<MediaListItem> Files);

/// <summary>
/// Folder tree: create, browse, rename, move, and delete-to-trash.
/// See docs/folder-hierarchy-design.md.
/// </summary>
[ApiController]
[Authorize]
[Route("api/folders")]
public class FoldersController(
    AppDbContext db,
    FolderService folders,
    TrashService trash) : ControllerBase
{
    /// <summary>
    /// Everything inside one folder: subfolders, files, and the breadcrumb trail. Omit
    /// <paramref name="folderId"/> for the root. One call so the UI can render a whole view.
    /// </summary>
    [HttpGet("contents")]
    [ProducesResponseType<FolderContents>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FolderContents>> Contents(
        [FromQuery] Guid? folderId, CancellationToken ct)
    {
        var userId = User.UserId();

        FolderItem? current = null;
        var breadcrumbs = new List<Breadcrumb>();

        if (folderId is not null)
        {
            var folder = await folders.OwnedAsync(userId, folderId.Value, ct);
            if (folder is null) return NotFound();

            current = ToItem(folder);
            breadcrumbs = (await folders.BreadcrumbsAsync(userId, folder.Id, ct))
                .Select(f => new Breadcrumb(f.Id, f.Name)).ToList();
        }

        // The hot query, and the reason the hierarchy needs no materialized path: listing a
        // folder's children is a plain indexed lookup on (OwnerId, ParentId).
        var children = await db.Folders
            .Where(f => f.OwnerId == userId && f.ParentId == folderId)
            .OrderBy(f => f.NameLower)
            .Select(f => new FolderItem(f.Id, f.Name, f.ParentId, f.CreatedAt, f.UpdatedAt))
            .ToListAsync(ct);

        var rows = await db.MediaFiles
            .Where(m => m.OwnerId == userId && m.FolderId == folderId && m.Status == MediaStatus.Ready)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new { m.Id, m.OriginalName, m.ContentType, m.SizeBytes, m.FolderId, m.CreatedAt })
            .ToListAsync(ct);

        // PreviewKind is an in-memory allowlist lookup, so it is applied after materialising.
        var files = rows.Select(m => new MediaListItem(
            m.Id, m.OriginalName, m.ContentType, m.SizeBytes, m.FolderId, m.CreatedAt,
            PreviewPolicy.KindName(m.ContentType))).ToList();

        return new FolderContents(current, breadcrumbs, children, files);
    }

    [HttpPost]
    [ProducesResponseType<FolderItem>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FolderItem>> Create(CreateFolderRequest req, CancellationToken ct)
    {
        try
        {
            var folder = await folders.CreateAsync(User.UserId(), req.Name, req.ParentId, ct);
            return CreatedAtAction(nameof(Contents), new { folderId = folder.Id }, ToItem(folder));
        }
        catch (FolderException ex)
        {
            return Problem(ex.Message, statusCode: ex.StatusCode);
        }
    }

    /// <summary>
    /// Rename. The stored name may differ from the requested one — a collision is resolved by
    /// suffixing rather than by failing, so always use the name in the response.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<FolderItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FolderItem>> Rename(Guid id, RenameFolderRequest req, CancellationToken ct)
    {
        try
        {
            return ToItem(await folders.RenameAsync(User.UserId(), id, req.Name, ct));
        }
        catch (FolderException ex)
        {
            return Problem(ex.Message, statusCode: ex.StatusCode);
        }
    }

    /// <summary>Move to another folder, or to the root with a null parentId.</summary>
    [HttpPost("{id:guid}/move")]
    [ProducesResponseType<FolderItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FolderItem>> Move(Guid id, MoveFolderRequest req, CancellationToken ct)
    {
        try
        {
            return ToItem(await folders.MoveAsync(User.UserId(), id, req.ParentId, ct));
        }
        catch (FolderException ex)
        {
            return Problem(ex.Message, statusCode: ex.StatusCode);
        }
    }

    /// <summary>
    /// Move the folder and everything under it to the trash. Recursive and instant: one
    /// transaction, no object-storage calls, fully reversible until the retention window expires.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            await trash.TrashFolderAsync(User.UserId(), id, ct);
            return NoContent();
        }
        catch (TrashException ex)
        {
            return Problem(ex.Message, statusCode: ex.StatusCode);
        }
    }

    private static FolderItem ToItem(Folder f) =>
        new(f.Id, f.Name, f.ParentId, f.CreatedAt, f.UpdatedAt);
}
