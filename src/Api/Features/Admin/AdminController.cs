using Keepr.Api.Data;
using Keepr.Api.Domain;
using Keepr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Keepr.Api.Features.Admin;

/// <summary>One account as an admin sees it. <paramref name="Role"/> is the enum name
/// ("User"/"Admin"). <paramref name="ActiveSessions"/> is live sessions (not revoked, not expired).</summary>
public record AdminUserListItem(
    Guid Id, string Email, string Role, long QuotaBytes, long UsedBytes, long RemainingBytes,
    DateTimeOffset CreatedAt, int ActiveSessions);

/// <summary>One account in full. Adds <paramref name="TrashedBytes"/> — the part of
/// <paramref name="UsedBytes"/> held by trashed files, still counted until purged.</summary>
public record AdminUserDetail(
    Guid Id, string Email, string Role, long QuotaBytes, long UsedBytes, long RemainingBytes,
    long TrashedBytes, DateTimeOffset CreatedAt, int ActiveSessions);

/// <summary>A page of results plus the total, so an admin table can show "showing 1–50 of 213".</summary>
public record PagedResponse<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// <param name="QuotaBytes">The account's new total storage allowance, in bytes. Must be &gt;= 0.
/// Setting it below current usage is allowed — it simply blocks further uploads until space is freed.</param>
public record UpdateQuotaRequest(long QuotaBytes);

/// <summary>
/// Account administration, restricted to admins by the "Admin" policy. A non-admin caller is
/// authenticated but forbidden (403); an anonymous caller is unauthorized (401). See
/// docs/admin-console-design.md.
/// </summary>
[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class AdminController(
    AppDbContext db,
    TrashService trash,
    AdminAuditService audit,
    TimeProvider clock) : ControllerBase
{
    private const int MaxPageSize = 200;
    private const int DefaultPageSize = 50;

    /// <summary>Lists accounts, newest first, for the admin's account table.</summary>
    [HttpGet("users")]
    [ProducesResponseType<PagedResponse<AdminUserListItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<AdminUserListItem>>> ListUsers(
        [FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var now = clock.GetUtcNow();

        var total = await db.Users.CountAsync(ct);

        // Compute the offset in long space and cap it at the row count: a huge `page` would
        // otherwise overflow int in (page - 1) * pageSize and wrap to a negative OFFSET, which
        // Postgres rejects. Past the last page this caps at Skip(total) — an empty final page.
        var skip = (int)Math.Min((long)(page - 1) * pageSize, total);

        var items = await db.Users
            .OrderByDescending(u => u.CreatedAt)
            .Skip(skip)
            .Take(pageSize)
            .Select(u => new AdminUserListItem(
                u.Id, u.Email, u.Role.ToString(), u.QuotaBytes, u.UsedBytes,
                u.QuotaBytes - u.UsedBytes < 0 ? 0 : u.QuotaBytes - u.UsedBytes,
                u.CreatedAt,
                db.Sessions.Count(s => s.UserId == u.Id && s.RevokedAt == null && s.ExpiresAt > now)))
            .ToListAsync(ct);

        return Ok(new PagedResponse<AdminUserListItem>(items, total, page, pageSize));
    }

    /// <summary>One account's detail, for the admin's account drawer.</summary>
    [HttpGet("users/{id:guid}")]
    [ProducesResponseType<AdminUserDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminUserDetail>> GetUser(Guid id, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([id], ct);
        if (user is null) return NotFound();

        var trashed = await trash.TrashedBytesAsync(id, ct);
        var active = await db.Sessions.CountAsync(
            s => s.UserId == id && s.RevokedAt == null && s.ExpiresAt > clock.GetUtcNow(), ct);

        return new AdminUserDetail(
            user.Id, user.Email, user.Role.ToString(), user.QuotaBytes, user.UsedBytes,
            user.RemainingBytes, trashed, user.CreatedAt, active);
    }

    /// <summary>
    /// Sets a user's storage quota. Lowering it below current usage is permitted (it blocks new
    /// uploads without touching existing files); the response's <c>RemainingBytes</c> shows the
    /// result so the UI can warn when it lands at zero. Writes one audit entry.
    /// </summary>
    [HttpPatch("users/{id:guid}/quota")]
    [ProducesResponseType<AdminUserDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminUserDetail>> UpdateQuota(
        Guid id, UpdateQuotaRequest req, CancellationToken ct)
    {
        if (req.QuotaBytes < 0)
            return Problem("Quota must be zero or greater.", statusCode: StatusCodes.Status400BadRequest);

        var user = await db.Users.FindAsync([id], ct);
        if (user is null) return NotFound();

        var from = user.QuotaBytes;
        if (from != req.QuotaBytes)
        {
            user.QuotaBytes = req.QuotaBytes;
            // Audit row added, not saved: it commits in the same SaveChanges as the quota change,
            // so the two are all-or-nothing.
            audit.RecordQuotaChange(User.UserId(), ActorEmail(), user, from, req.QuotaBytes);
            await db.SaveChangesAsync(ct);
        }

        var trashed = await trash.TrashedBytesAsync(id, ct);
        var active = await db.Sessions.CountAsync(
            s => s.UserId == id && s.RevokedAt == null && s.ExpiresAt > clock.GetUtcNow(), ct);

        return new AdminUserDetail(
            user.Id, user.Email, user.Role.ToString(), user.QuotaBytes, user.UsedBytes,
            user.RemainingBytes, trashed, user.CreatedAt, active);
    }

    /// <summary>
    /// Kicks a user: revokes every session now, marks the account for deletion, and audits it —
    /// all synchronously — then returns 202. The background <c>AccountWipeService</c> hard-deletes
    /// their files (live and trashed, no recovery window) and removes the account. Guardrails: an
    /// admin cannot kick their own account, and the last remaining admin cannot be removed. See
    /// docs/admin-console-design.md §4.2/§4.3.
    /// </summary>
    [HttpDelete("users/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> KickUser(Guid id, CancellationToken ct)
    {
        if (id == User.UserId())
            return Problem("You cannot remove your own account.", statusCode: StatusCodes.Status400BadRequest);

        var user = await db.Users.FindAsync([id], ct);
        if (user is null) return NotFound();

        // Idempotent: a second kick while the wipe is still pending is a no-op, not an error.
        if (user.DeletionRequestedAt is not null) return Accepted();

        // Never strand the instance with no admin. Count admins other than this one that aren't
        // themselves being deleted; if there are none, this kick would remove the last admin.
        if (user.Role == Role.Admin)
        {
            var otherAdmins = await db.Users.CountAsync(
                u => u.Role == Role.Admin && u.Id != id && u.DeletionRequestedAt == null, ct);
            if (otherAdmins == 0)
                return Problem("Cannot remove the last admin.", statusCode: StatusCodes.Status409Conflict);
        }

        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Take the same FOR UPDATE lock the login path holds while issuing a session, so a
        // concurrent login cannot slip a new session in between this revocation and the commit.
        // See docs/admin-console-design.md §4.2 and AuthController.Login.
        await db.Database.ExecuteSqlRawAsync(
            $"SELECT 1 FROM {AppDbContext.Schema}.\"Users\" WHERE \"Id\" = {{0}} FOR UPDATE", [id], ct);

        // Re-check under the lock. The pre-lock idempotency check above can race a concurrent kick
        // that set DeletionRequestedAt (or finished the wipe) after we loaded the user; the FOR
        // UPDATE serialises us behind it, so reload and bail idempotently rather than revoke again
        // and write a duplicate UserKicked audit row.
        await db.Entry(user).ReloadAsync(ct);
        if (db.Entry(user).State == EntityState.Detached || user.DeletionRequestedAt is not null)
            return Accepted();

        // Access is gone the instant this commits, before a single byte is touched.
        await db.Sessions
            .Where(s => s.UserId == id && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);

        user.DeletionRequestedAt = now;
        audit.RecordUserKicked(User.UserId(), ActorEmail(), user.Id, user.Email);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Accepted();
    }

    private string ActorEmail() => User.FindFirst(KeeprClaims.Email)?.Value ?? string.Empty;
}
