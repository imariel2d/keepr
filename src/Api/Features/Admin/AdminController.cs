using Keepr.Api.Data;
using Keepr.Api.Domain;
using Keepr.Api.Features.Auth;
using Keepr.Api.Features.Email;
using Keepr.Api.Features.Invites;
using Keepr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Keepr.Api.Features.Admin;

/// <summary>One account as an admin sees it. <paramref name="Role"/> is the enum name
/// ("User"/"Admin"). <paramref name="ActiveSessions"/> is live sessions (not revoked, not expired).
/// <paramref name="Pending"/> is an invited-but-unclaimed account (no password yet) — it can't sign
/// in, and its invite can be resent. See docs/feature-36-account-provisioning.md §8.1.</summary>
public record AdminUserListItem(
    Guid Id, string Email, string Role, long QuotaBytes, long UsedBytes, long RemainingBytes,
    DateTimeOffset CreatedAt, int ActiveSessions, bool Pending);

/// <summary>One account in full. Adds <paramref name="TrashedBytes"/> — the part of
/// <paramref name="UsedBytes"/> held by trashed files, still counted until purged.</summary>
public record AdminUserDetail(
    Guid Id, string Email, string Role, long QuotaBytes, long UsedBytes, long RemainingBytes,
    long TrashedBytes, DateTimeOffset CreatedAt, int ActiveSessions, bool Pending);

/// <summary>A page of results plus the total, so an admin table can show "showing 1–50 of 213".</summary>
public record PagedResponse<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// <param name="QuotaBytes">The account's new total storage allowance, in bytes. Must be &gt;= 0.
/// Setting it below current usage is allowed — it simply blocks further uploads until space is freed.</param>
public record UpdateQuotaRequest(long QuotaBytes);

/// <param name="Email">Address for the new account; matched case-insensitively.</param>
/// <param name="Role">"User" or "Admin".</param>
/// <param name="SendInvite">True → email a claim link (requires a configured mail sender);
/// false → the account is active immediately with <paramref name="Password"/>.</param>
/// <param name="Password">Required when <paramref name="SendInvite"/> is false; ignored otherwise.</param>
public record CreateUserRequest(string Email, string Role, bool SendInvite, string? Password);

/// <summary>The provisioned account, plus how it was activated. <paramref name="InviteEmailSent"/>
/// is false for direct-password accounts and for invites whose email failed to send (the account
/// still exists — resend it). See docs/feature-36-account-provisioning.md §4/§8.3.</summary>
public record CreateUserResponse(AdminUserDetail Account, bool Invited, bool InviteEmailSent);

/// <param name="Role">The account's new role: "User" or "Admin".</param>
public record UpdateRoleRequest(string Role);

/// <param name="SendLink">true → email the user a reset link (requires a configured sender and a
/// verified account); false → set a new password directly. See docs/feature-26-password-reset.md §6.</param>
/// <param name="Password">Required when <paramref name="SendLink"/> is false; ignored otherwise.</param>
public record ResetUserRequest(bool SendLink, string? Password);

/// <summary>
/// Account administration, restricted to admins by the "Admin" policy. A non-admin caller is
/// authenticated but forbidden (403); an anonymous caller is unauthorized (401). See
/// docs/feature-34-admin-console.md.
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
    CredentialValidator credentials,
    InviteService invites,
    PasswordResetService resets,
    SessionService sessions,
    EmailSettingsService emailSettings,
    IOptions<QuotaOptions> quota,
    TimeProvider clock,
    ILogger<AdminController> log) : ControllerBase
{
    private const int MaxPageSize = 200;
    private const int DefaultPageSize = 50;

    // One fixed key that every admin-count-guarded mutation (demote here; a future admin-kick)
    // locks on, so the last-admin invariant is serialized across different target rows. See §5.
    private const long AdminSetLockKey = 0x_4B50_4144; // "KPAD"

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

        // Exclude accounts already kicked (deletion pending): they are gone from the admin's point
        // of view, and the background wipe removes the row shortly. Showing them would make a just-
        // removed account linger in the table and invite a confusing second kick.
        var visible = db.Users.Where(u => u.DeletionRequestedAt == null);

        var total = await visible.CountAsync(ct);

        // Compute the offset in long space and cap it at the row count: a huge `page` would
        // otherwise overflow int in (page - 1) * pageSize and wrap to a negative OFFSET, which
        // Postgres rejects. Past the last page this caps at Skip(total) — an empty final page.
        var skip = (int)Math.Min((long)(page - 1) * pageSize, total);

        var items = await visible
            .OrderByDescending(u => u.CreatedAt)
            .Skip(skip)
            .Take(pageSize)
            .Select(u => new AdminUserListItem(
                u.Id, u.Email, u.Role.ToString(), u.QuotaBytes, u.UsedBytes,
                u.QuotaBytes - u.UsedBytes < 0 ? 0 : u.QuotaBytes - u.UsedBytes,
                u.CreatedAt,
                db.Sessions.Count(s => s.UserId == u.Id && s.RevokedAt == null && s.ExpiresAt > now),
                u.PasswordHash == null))
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
        // A kicked account (deletion pending) is gone from the admin's view — see ListUsers.
        if (user is null || user.DeletionRequestedAt is not null) return NotFound();

        return await BuildDetailAsync(user, ct);
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

        return await BuildDetailAsync(user, ct);
    }

    /// <summary>
    /// Kicks a user: revokes every session now, marks the account for deletion, and audits it —
    /// all synchronously — then returns 202. The background <c>AccountWipeService</c> hard-deletes
    /// their files (live and trashed, no recovery window) and removes the account. Guardrails: an
    /// admin cannot kick their own account, and the last remaining admin cannot be removed. See
    /// docs/feature-34-admin-console.md §4.2/§4.3.
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

        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Serialize the admin-set invariant against demotion (and concurrent kicks) on the same
        // shared lock the demote path uses. Without it, A kicking B while B demotes A would let both
        // pass their last-admin check and leave zero admins. Taken before the last-admin count
        // below. See docs/feature-36-account-provisioning.md §5 and feature-34-admin-console.md §4.3.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [AdminSetLockKey], ct);

        // Also take the FOR UPDATE the login path holds while issuing a session, so a concurrent
        // login cannot slip a new session in between this revocation and the commit. See
        // docs/feature-34-admin-console.md §4.2 and AuthController.Login.
        await db.Database.ExecuteSqlRawAsync(
            $"SELECT 1 FROM {AppDbContext.Schema}.\"Users\" WHERE \"Id\" = {{0}} FOR UPDATE", [id], ct);

        // Re-check under the locks. A concurrent kick may have set DeletionRequestedAt (or finished
        // the wipe) after we loaded the user; reload and bail idempotently rather than revoke again
        // and write a duplicate UserKicked audit row.
        await db.Entry(user).ReloadAsync(ct);
        if (db.Entry(user).State == EntityState.Detached || user.DeletionRequestedAt is not null)
            return Accepted();

        // Never strand the instance with no admin — checked under the advisory lock so a concurrent
        // demote can't also pass. Count admins other than this one that aren't themselves pending.
        if (user.Role == Role.Admin)
        {
            var otherAdmins = await db.Users.CountAsync(
                u => u.Role == Role.Admin && u.Id != id && u.DeletionRequestedAt == null, ct);
            if (otherAdmins == 0)
                return Problem("Cannot remove the last admin.", statusCode: StatusCodes.Status409Conflict);
        }

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

    /// <summary>
    /// Provisions a new account. Direct mode (<c>sendInvite: false</c>) creates it active with the
    /// given password and forces a change on first sign-in (the admin knows it). Invite mode
    /// (<c>sendInvite: true</c>) creates a pending account with no password and emails a claim link;
    /// it requires a configured mail sender. Both write a <c>UserCreated</c> audit entry. See
    /// docs/feature-36-account-provisioning.md §4.
    /// </summary>
    [HttpPost("users")]
    [ProducesResponseType<CreateUserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateUserResponse>> CreateUser(CreateUserRequest req, CancellationToken ct)
    {
        var email = (req.Email ?? string.Empty).Trim().ToLowerInvariant();

        if (!Enum.TryParse<Role>(req.Role, ignoreCase: true, out var role) || !Enum.IsDefined(role))
            return Problem("Role must be 'User' or 'Admin'.", statusCode: StatusCodes.Status400BadRequest);

        if (req.SendInvite)
        {
            // Never silently no-op an invite: refuse if no real sender is configured (§4.2). Tagged
            // with a machine-readable code so the client can tell this apart from the *other* 409
            // this endpoint returns (duplicate email) and show the right message.
            if (!await emailSettings.IsEnabledAsync(ct))
            {
                var pd = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Detail = "Email delivery is not configured — set a password instead."
                };
                pd.Extensions["code"] = "email_not_configured";
                return Conflict(pd);
            }
            if (EmailPolicy.Validate(email) is { } emailError)
                return BadRequest(FieldError("email", emailError));
        }
        else if (await credentials.ValidateAsync(email, req.Password, ct) is { } errors)
        {
            return BadRequest(new ValidationProblemDetails(errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Detail = string.Join(" ", errors.Values.SelectMany(v => v))
            });
        }

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            return Problem("Email already registered.", statusCode: StatusCodes.Status409Conflict);

        var user = new User
        {
            Email = email,
            Role = role,
            QuotaBytes = quota.Value.DefaultBytes,
            // Invite mode leaves the hash null until the recipient claims it (§8.1). Direct mode sets
            // it and forces a change on first sign-in, since the admin picked (and knows) it.
            PasswordHash = req.SendInvite ? null : BCrypt.Net.BCrypt.HashPassword(req.Password),
            MustChangePassword = !req.SendInvite
        };
        db.Users.Add(user);

        string? rawToken = null;
        if (req.SendInvite)
        {
            var (invite, token) = await invites.BuildAsync(user.Id, ct);
            db.AccountInvites.Add(invite);
            rawToken = token;
        }

        audit.RecordUserCreated(User.UserId(), ActorEmail(), user, invited: req.SendInvite);

        // Account + invite + audit commit together; the email is sent only after (§8.3).
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Lost the check-then-act race on the unique Email index (two concurrent creates, or an
            // impatient double-click). Report the same 409 the AnyAsync pre-check would, not a 500.
            return Problem("Email already registered.", statusCode: StatusCodes.Status409Conflict);
        }

        var emailSent = false;
        if (req.SendInvite && rawToken is not null)
        {
            try
            {
                await invites.SendAsync(user.Email, rawToken, await ActorDisplayNameAsync(ct), ct);
                emailSent = true;
            }
            catch (Exception ex)
            {
                // Non-fatal: the account exists (committed above), the admin can resend. A failed
                // send must not roll back a valid account creation.
                log.LogError(ex,
                    "Account {Email} created but the invite email failed to send; it can be resent.",
                    user.Email);
            }
        }

        var detail = await BuildDetailAsync(user, ct);
        return CreatedAtAction(nameof(GetUser), new { id = user.Id },
            new CreateUserResponse(detail, req.SendInvite, emailSent));
    }

    /// <summary>
    /// Changes a user's role. Guardrails: an admin cannot demote themselves, and the last remaining
    /// admin cannot be demoted (409). The last-admin check is serialized on a shared advisory lock
    /// so two concurrent demotes can't both pass it. See docs/feature-36-account-provisioning.md §5.
    /// </summary>
    [HttpPatch("users/{id:guid}/role")]
    [ProducesResponseType<AdminUserDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminUserDetail>> UpdateRole(
        Guid id, UpdateRoleRequest req, CancellationToken ct)
    {
        if (!Enum.TryParse<Role>(req.Role, ignoreCase: true, out var newRole) || !Enum.IsDefined(newRole))
            return Problem("Role must be 'User' or 'Admin'.", statusCode: StatusCodes.Status400BadRequest);

        if (AdminInvariants.IsSelfDemotion(id == User.UserId(), newRole))
            return Problem("You cannot demote your own account.", statusCode: StatusCodes.Status400BadRequest);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Serialize the admin-count invariant on one lock: a single-row FOR UPDATE wouldn't help,
        // since two admins demoting each other lock different rows and never contend (§5).
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [AdminSetLockKey], ct);

        var user = await db.Users.FindAsync([id], ct);
        if (user is null || user.DeletionRequestedAt is not null) return NotFound();

        if (user.Role != newRole)
        {
            // A demotion must not strand the instance with zero admins.
            if (user.Role == Role.Admin && newRole == Role.User)
            {
                var otherAdmins = await db.Users.CountAsync(
                    u => u.Role == Role.Admin && u.Id != id && u.DeletionRequestedAt == null, ct);
                if (AdminInvariants.WouldRemoveLastAdmin(user.Role, newRole, otherAdmins))
                    return Problem("Cannot remove the last admin.", statusCode: StatusCodes.Status409Conflict);
            }

            var from = user.Role;
            user.Role = newRole;
            audit.RecordRoleChange(User.UserId(), ActorEmail(), user, from, newRole);
            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);
        return await BuildDetailAsync(user, ct);
    }

    /// <summary>
    /// Re-sends the claim invite for a pending (unclaimed) account, superseding any earlier link.
    /// Requires a configured mail sender; refused if the account has already been claimed. See
    /// docs/feature-36-account-provisioning.md §8.5.
    /// </summary>
    [HttpPost("users/{id:guid}/invite")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> ResendInvite(Guid id, CancellationToken ct)
    {
        if (!await emailSettings.IsEnabledAsync(ct))
            return Problem("Email delivery is not configured.", statusCode: StatusCodes.Status409Conflict);

        var user = await db.Users.FindAsync([id], ct);
        if (user is null || user.DeletionRequestedAt is not null) return NotFound();

        // Only an unclaimed account (null hash) has an invite to resend.
        if (user.PasswordHash is not null)
            return Problem("This account has already been claimed.", statusCode: StatusCodes.Status409Conflict);

        await invites.RemoveExistingAsync(user.Id, ct);
        var (invite, token) = await invites.BuildAsync(user.Id, ct);
        db.AccountInvites.Add(invite);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Lost a race with a concurrent resend: the one-live-invite-per-account unique index
            // rejected this second insert (the other request's link stands). Report it rather than
            // 500; the admin can retry. See docs/feature-36-account-provisioning.md §8.2/§8.5.
            return Problem("Another invite for this account was just issued. Try again.",
                statusCode: StatusCodes.Status409Conflict);
        }

        try
        {
            await invites.SendAsync(user.Email, token, await ActorDisplayNameAsync(ct), ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Resent invite for {Email} failed to send.", user.Email);
            return Problem("Could not send the invite email. Try again.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        return NoContent();
    }

    /// <summary>
    /// Resets a user's password — the fallback the "contact your admin" copy points at, and the only
    /// recovery for unverified or no-email accounts. Direct mode (<c>sendLink:false</c>) sets a new
    /// password and forces a change on the user's first sign-in (the admin knows it); link mode
    /// (<c>sendLink:true</c>) emails a reset link and requires both a configured sender and a
    /// <b>verified</b> account (emailing a link to an unverified address is the §9 takeover hazard).
    /// Both revoke the target's existing sessions. Refused for a pending (unclaimed) account. An admin
    /// may reset their own account; self-resets skip the forced-change flag. See
    /// docs/feature-26-password-reset.md §6.
    /// </summary>
    [HttpPost("users/{id:guid}/reset-password")]
    [ProducesResponseType<AdminUserDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> ResetPassword(Guid id, ResetUserRequest req, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([id], ct);
        if (user is null || user.DeletionRequestedAt is not null) return NotFound();

        // Only an active (claimed) account has a password to reset. A pending account (null hash) has
        // no password yet — it uses resend-invite, not reset.
        if (user.PasswordHash is null)
            return Problem("This account hasn't been claimed yet — resend its invite instead.",
                statusCode: StatusCodes.Status409Conflict);

        if (req.SendLink)
        {
            // Never silently no-op a link, and never send one to an unverified inbox. Both refusals
            // carry a machine-readable code so the client can tell them apart and show the right copy.
            if (!await emailSettings.IsEnabledAsync(ct))
            {
                var pd = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Detail = "Email delivery is not configured — set a password instead."
                };
                pd.Extensions["code"] = "email_not_configured";
                return Conflict(pd);
            }
            if (!user.EmailVerified)
            {
                var pd = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Detail = "This account's email isn't verified — set a password directly instead."
                };
                pd.Extensions["code"] = "email_unverified";
                return Conflict(pd);
            }

            await resets.RemoveExistingAsync(user.Id, ct);
            var (token, raw) = resets.Build(user.Id);
            db.PasswordResetTokens.Add(token);
            audit.RecordPasswordReset(User.UserId(), ActorEmail(), user, "link");
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                // Lost a race with a concurrent reset: the one-live-token index rejected this insert.
                return Problem("Another reset for this account was just issued. Try again.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            try
            {
                await resets.SendAsync(user.Email, raw, ct);
            }
            catch (Exception ex)
            {
                // The admin explicitly asked to send, so surface the failure (unlike self-service).
                log.LogError(ex, "Admin-initiated reset email for {Email} failed to send.", user.Email);
                return Problem("Could not send the reset email. Try again.",
                    statusCode: StatusCodes.Status502BadGateway);
            }
            return Accepted();
        }

        // Direct mode: the admin sets a new password.
        if (await credentials.ValidatePasswordAsync(req.Password, user.Email, ct) is { } errors)
            return BadRequest(new ValidationProblemDetails(errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Detail = string.Join(" ", errors.Values.SelectMany(v => v))
            });

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password);
        // The admin knows this password, so force a rotation on the user's next sign-in — except when
        // an admin resets their own account, where there's nothing to rotate away from. Setting a
        // password directly proves nothing about the inbox, so EmailVerified is left untouched.
        user.MustChangePassword = id != User.UserId();
        // A reset boots the old credential everywhere.
        await sessions.RevokeAllForUserAsync(user.Id, ct);
        audit.RecordPasswordReset(User.UserId(), ActorEmail(), user, "direct");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Ok(await BuildDetailAsync(user, ct));
    }

    private async Task<AdminUserDetail> BuildDetailAsync(User user, CancellationToken ct)
    {
        var trashed = await trash.TrashedBytesAsync(user.Id, ct);
        var active = await db.Sessions.CountAsync(
            s => s.UserId == user.Id && s.RevokedAt == null && s.ExpiresAt > clock.GetUtcNow(), ct);

        return new AdminUserDetail(
            user.Id, user.Email, user.Role.ToString(), user.QuotaBytes, user.UsedBytes,
            user.RemainingBytes, trashed, user.CreatedAt, active, user.PasswordHash == null);
    }

    private static ValidationProblemDetails FieldError(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] })
        {
            Status = StatusCodes.Status400BadRequest,
            Detail = message
        };

    private string ActorEmail() => User.FindFirst(KeeprClaims.Email)?.Value ?? string.Empty;

    /// <summary>The inviting admin's display name for the invite email — their first/last name joined
    /// (whichever parts are set), or null when they have no name. The invite deliberately shows a name,
    /// never an email: with no name the template falls back to a generic "you've been invited" line.</summary>
    private async Task<string?> ActorDisplayNameAsync(CancellationToken ct)
    {
        var actor = await db.Users
            .Where(u => u.Id == User.UserId())
            .Select(u => new { u.FirstName, u.LastName })
            .FirstOrDefaultAsync(ct);
        return actor is null ? null : Domain.User.DisplayName(actor.FirstName, actor.LastName);
    }
}
