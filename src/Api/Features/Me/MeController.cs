using Keepr.Api.Data;
using Keepr.Api.Features.Auth;
using Keepr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Keepr.Api.Features.Me;

/// <summary>
/// <see cref="TrashedBytes"/> is the part of <see cref="UsedBytes"/> held by trashed files.
/// Trash still counts against quota until it is purged — surfacing it lets the UI answer the
/// inevitable "I deleted everything and I'm still full".
/// </summary>
public record UsageResponse(long QuotaBytes, long UsedBytes, long RemainingBytes, long TrashedBytes);

/// <summary>The signed-in account's profile. <paramref name="MustChangePassword"/> tells the SPA to
/// route to the set-password step before anything else (§7.3).</summary>
public record ProfileResponse(
    string Email, string? FirstName, string? LastName, string Role, bool MustChangePassword);

/// <param name="FirstName">Given name, or null/blank to clear it.</param>
/// <param name="LastName">Family name, or null/blank to clear it.</param>
public record UpdateProfileRequest(string? FirstName, string? LastName);

/// <param name="CurrentPassword">The existing password, re-verified before the change.</param>
/// <param name="NewPassword">The replacement, held to the registration password rules.</param>
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

[ApiController]
[Authorize]
[Route("api/me")]
public class MeController(
    AppDbContext db,
    TrashService trash,
    CredentialValidator credentials,
    SessionCookie cookie,
    TimeProvider clock) : ControllerBase
{
    /// <summary>Powers the always-visible "space remaining" meter.</summary>
    [HttpGet("usage")]
    [ProducesResponseType<UsageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UsageResponse>> Usage(CancellationToken ct)
    {
        var userId = User.UserId();
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null) return NotFound();

        var trashed = await trash.TrashedBytesAsync(userId, ct);
        return new UsageResponse(user.QuotaBytes, user.UsedBytes, user.RemainingBytes, trashed);
    }

    /// <summary>The profile screen's data (#29), and the forced-change signal (§7.3).</summary>
    [HttpGet("profile")]
    [ProducesResponseType<ProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProfileResponse>> GetProfile(CancellationToken ct)
    {
        var user = await db.Users.FindAsync([User.UserId()], ct);
        if (user is null) return NotFound();

        return new ProfileResponse(
            user.Email, user.FirstName, user.LastName, user.Role.ToString(), user.MustChangePassword);
    }

    /// <summary>Updates the account's display name (#29). Blank fields clear the stored value.</summary>
    [HttpPatch("profile")]
    [ProducesResponseType<ProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProfileResponse>> UpdateProfile(
        UpdateProfileRequest req, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([User.UserId()], ct);
        if (user is null) return NotFound();

        // Full replace of BOTH name fields, not a partial PATCH: a missing field and an explicitly
        // cleared field both arrive as null, so every call must send the whole pair. A blank value
        // clears the stored name by design. Don't "fix" a caller into sending only one field — that
        // would silently wipe the other.
        user.FirstName = Normalize(req.FirstName);
        user.LastName = Normalize(req.LastName);
        await db.SaveChangesAsync(ct);

        return new ProfileResponse(
            user.Email, user.FirstName, user.LastName, user.Role.ToString(), user.MustChangePassword);
    }

    /// <summary>
    /// Changes the password: verifies the current one, applies the password rules to the new one,
    /// re-hashes, clears any forced-change flag, and signs the account out everywhere else (its
    /// other sessions are revoked; this one stays). This is the #28 core. See
    /// docs/feature-36-account-provisioning.md §7.2.
    /// </summary>
    [HttpPost("password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([User.UserId()], ct);
        if (user is null) return NotFound();

        // A null hash means an unclaimed account (it shouldn't reach an authed endpoint), but guard
        // anyway so Verify never sees null.
        if (user.PasswordHash is null || !BCrypt.Net.BCrypt.Verify(req.CurrentPassword, user.PasswordHash))
            return Problem("Your current password is incorrect.", statusCode: StatusCodes.Status400BadRequest);

        if (await credentials.ValidatePasswordAsync(req.NewPassword, user.Email, ct) is { } errors)
            return BadRequest(new ValidationProblemDetails(errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Detail = string.Join(" ", errors.Values.SelectMany(v => v))
            });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        user.MustChangePassword = false;
        await db.SaveChangesAsync(ct);

        // "Sign out everywhere else": revoke this user's other live sessions, keeping the current
        // one (matched by the cookie's token hash) so the caller isn't logged out mid-change.
        var currentToken = Request.Cookies[cookie.Name];
        var currentHash = string.IsNullOrEmpty(currentToken) ? null : SecureToken.Hash(currentToken);
        await db.Sessions
            .Where(s => s.UserId == user.Id && s.RevokedAt == null
                        && (currentHash == null || s.TokenHash != currentHash))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, clock.GetUtcNow()), ct);

        return NoContent();
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
