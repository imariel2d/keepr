using Keepr.Api.Data;
using Keepr.Api.Domain;
using Keepr.Api.Features.Media;
using Keepr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Keepr.Api.Features.Search;

/// <summary>
/// A search hit. <see cref="Location"/> is the containing folder's path as ancestor names, top to
/// bottom (empty at the root) — the client prepends its own "My Files" label and joins them, so
/// the display copy stays on the client.
/// </summary>
public record SearchFolderItem(
    Guid Id, string Name, Guid? ParentId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<string> Location);

/// <summary>A file hit: a <see cref="MediaListItem"/> plus where it lives (see <see cref="SearchFolderItem"/>).</summary>
public record SearchFileItem(
    Guid Id, string OriginalName, string? ContentType, long SizeBytes, Guid? FolderId,
    DateTimeOffset CreatedAt, string? PreviewKind, IReadOnlyList<string> Location);

/// <summary>Folders first, then files — the same order the grid renders them in.</summary>
public record SearchResults(
    IReadOnlyList<SearchFolderItem> Folders, IReadOnlyList<SearchFileItem> Files);

/// <summary>
/// Owner-scoped name search across the whole tree. Matches files and folders by a case-insensitive
/// substring on their pre-lowercased name columns; trash is excluded by the soft-delete query
/// filters. See docs/search-design.md.
/// </summary>
[ApiController]
[Authorize]
[Route("api/search")]
public class SearchController(AppDbContext db) : ControllerBase
{
    /// <summary>At most this many of each kind — payload sanity, not a security bound (see design §2.1).</summary>
    private const int Cap = 200;

    /// <summary>
    /// Files and folders whose name contains <paramref name="q"/>. Empty/whitespace <paramref name="q"/>
    /// is a 400: an empty box means "browse", so the client never searches in that state.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<SearchResults>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SearchResults>> Search([FromQuery] string? q, CancellationToken ct)
    {
        var term = q?.Trim();
        if (string.IsNullOrEmpty(term))
            return Problem("A search term is required.", statusCode: StatusCodes.Status400BadRequest);

        var userId = User.UserId();
        var pattern = $"%{LikeEscape.Escape(term)}%";

        // The soft-delete query filters keep trash out of both of these automatically; the file
        // filter also demands Ready, so pending/failed uploads never surface.
        var folderRows = await db.Folders
            .Where(f => f.OwnerId == userId && EF.Functions.Like(f.NameLower, pattern, "\\"))
            .OrderBy(f => f.NameLower)
            .Take(Cap)
            .Select(f => new { f.Id, f.Name, f.ParentId, f.CreatedAt, f.UpdatedAt })
            .ToListAsync(ct);

        var fileRows = await db.MediaFiles
            .Where(m => m.OwnerId == userId && m.Status == MediaStatus.Ready
                        && EF.Functions.Like(m.OriginalNameLower, pattern, "\\"))
            .OrderByDescending(m => m.CreatedAt)
            .Take(Cap)
            .Select(m => new { m.Id, m.OriginalName, m.ContentType, m.SizeBytes, m.FolderId, m.CreatedAt })
            .ToListAsync(ct);

        // One cheap read of the owner's folder skeleton lets us render any item's path in memory,
        // instead of a recursive breadcrumb CTE per result. A personal drive has few folders.
        var skeleton = await db.Folders
            .Where(f => f.OwnerId == userId)
            .Select(f => new { f.Id, f.Name, f.ParentId })
            .ToListAsync(ct);
        var parents = skeleton.ToDictionary(f => f.Id, f => (f.Name, f.ParentId));

        IReadOnlyList<string> PathTo(Guid? folderId)
        {
            var parts = new List<string>();
            var seen = new HashSet<Guid>(); // belt-and-braces: the tree has a cycle guard already
            var cur = folderId;
            while (cur is Guid id && seen.Add(id) && parents.TryGetValue(id, out var node))
            {
                parts.Add(node.Name);
                cur = node.ParentId;
            }
            parts.Reverse();
            return parts;
        }

        var folders = folderRows
            .Select(f => new SearchFolderItem(
                f.Id, f.Name, f.ParentId, f.CreatedAt, f.UpdatedAt, PathTo(f.ParentId)))
            .ToList();

        var files = fileRows
            .Select(m => new SearchFileItem(
                m.Id, m.OriginalName, m.ContentType, m.SizeBytes, m.FolderId, m.CreatedAt,
                PreviewPolicy.KindName(m.ContentType), PathTo(m.FolderId)))
            .ToList();

        return new SearchResults(folders, files);
    }
}
