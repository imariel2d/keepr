using Keepr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Keepr.Api.Features.Trash;

public record TrashItemResponse(
    Guid Id, string Kind, string Name, long SizeBytes,
    DateTimeOffset DeletedAt, DateTimeOffset PurgesAt);

public record RestoreResponse(Guid Id, string Name);

/// <summary>
/// The trash: what's in it, restoring, and permanent deletion. Items land here from
/// DELETE /api/folders/{id} and DELETE /api/media/{id}. See docs/trash-soft-delete-design.md.
/// </summary>
[ApiController]
[Authorize]
[Route("api/trash")]
public class TrashController(
    TrashService trash,
    IOptions<CleanupOptions> cleanup) : ControllerBase
{
    private int RetentionDays => cleanup.Value.TrashRetentionDays;

    /// <summary>
    /// Items the user deleted directly — a trashed folder appears once, not once per file inside.
    /// <c>purgesAt</c> is computed server-side so the client never has to know the retention rule.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TrashItemResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TrashItemResponse>>> List(CancellationToken ct)
    {
        var entries = await trash.ListAsync(User.UserId(), RetentionDays, ct);
        return Ok(entries.Select(e => new TrashItemResponse(
            e.Id, e.Kind.ToString().ToLowerInvariant(), e.Name, e.SizeBytes, e.DeletedAt, e.PurgesAt)).ToList());
    }

    /// <summary>
    /// Restore an item and everything trashed alongside it. The name in the response may differ
    /// from the original: if something took the name meanwhile, the restore is suffixed rather
    /// than refused. An item whose folder is gone comes back to the root.
    /// </summary>
    [HttpPost("{id:guid}/restore")]
    [ProducesResponseType<RestoreResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RestoreResponse>> Restore(Guid id, CancellationToken ct)
    {
        try
        {
            return new RestoreResponse(id, await trash.RestoreAsync(User.UserId(), id, ct));
        }
        catch (TrashException ex)
        {
            return Problem(ex.Message, statusCode: ex.StatusCode);
        }
    }

    /// <summary>Permanently delete one item now, without waiting out the retention window.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Purge(Guid id, CancellationToken ct)
    {
        try
        {
            await trash.PurgeAsync(User.UserId(), id, ct);
            return NoContent();
        }
        catch (TrashException ex)
        {
            return Problem(ex.Message, statusCode: ex.StatusCode);
        }
    }

    /// <summary>Permanently delete everything in the trash. This is what frees quota immediately.</summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> EmptyTrash(CancellationToken ct)
    {
        var userId = User.UserId();
        foreach (var entry in await trash.ListAsync(userId, RetentionDays, ct))
        {
            // Independent purges: one failing item shouldn't strand the rest.
            try
            {
                await trash.PurgeAsync(userId, entry.Id, ct);
            }
            catch (TrashException)
            {
                // Already gone (e.g. the sweeper got there first) — nothing to do.
            }
        }
        return NoContent();
    }
}
