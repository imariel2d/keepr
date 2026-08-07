using Keepr.Api.Data;
using Keepr.Api.Features.Auth;
using Keepr.Api.Features.Email;
using Keepr.Api.Features.Localization;
using Keepr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Keepr.Api.Features.Me;

/// <summary>
/// <see cref="TrashedBytes"/> is the part of <see cref="UsedBytes"/> held by trashed files.
/// Trash still counts against quota until it is purged — surfacing it lets the UI answer the
/// inevitable "I deleted everything and I'm still full".
/// </summary>
public record UsageResponse(long QuotaBytes, long UsedBytes, long RemainingBytes, long TrashedBytes);

/// <summary>The signed-in account's profile. <paramref name="MustChangePassword"/> tells the SPA to
/// route to the set-password step before anything else (§7.3). <paramref name="EmailVerified"/> and
/// <paramref name="PendingEmail"/> drive the change-email panel (#27): whether to show a verified badge,
/// and any in-flight change awaiting confirmation. <paramref name="PreferredLanguage"/> is the account's
/// chosen UI locale ("en"/"es"/"fr"), or null for the default (#30).</summary>
public record ProfileResponse(
    string Email, string? FirstName, string? LastName, string Role, bool MustChangePassword,
    bool EmailVerified, string? PendingEmail, string? PreferredLanguage);

/// <param name="FirstName">Given name, or null/blank to clear it.</param>
/// <param name="LastName">Family name, or null/blank to clear it.</param>
/// <param name="PreferredLanguage">Preferred UI locale — "en", "es", or "fr", or null/blank to clear it
/// back to the default (English). Any other value is rejected (400 <c>invalid_language</c>). Sent as
/// part of the full-profile replace, like the name fields (#30).</param>
public record UpdateProfileRequest(string? FirstName, string? LastName, string? PreferredLanguage);

/// <param name="CurrentPassword">The existing password, re-verified before the change.</param>
/// <param name="NewPassword">The replacement, held to the registration password rules.</param>
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <param name="NewEmail">The address to move the account to; matched case-insensitively, held to
/// <see cref="EmailPolicy"/>, and required to be free.</param>
/// <param name="CurrentPassword">The existing password, re-verified before the change (#27 §3).</param>
public record ChangeEmailRequest(string NewEmail, string CurrentPassword);

/// <summary>The staged (mail-on) change-email response: the address a confirmation link was sent to.
/// The change isn't applied until that link is confirmed (§5.1).</summary>
/// <param name="PendingEmail">The new address awaiting confirmation.</param>
public record EmailChangePendingResponse(string PendingEmail);

[ApiController]
[Authorize]
[Route("api/me")]
public class MeController(
    AppDbContext db,
    TrashService trash,
    CredentialValidator credentials,
    EmailChangeService emailChanges,
    EmailSettingsService emailSettings,
    SessionCookie cookie,
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<MeController> log) : ControllerBase
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

        return await ToProfileAsync(user, ct);
    }

    /// <summary>Updates the account's display name (#29) and preferred UI language (#30). Blank fields
    /// clear the stored value; an unsupported language is rejected.</summary>
    [HttpPatch("profile")]
    [ProducesResponseType<ProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProfileResponse>> UpdateProfile(
        UpdateProfileRequest req, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([User.UserId()], ct);
        if (user is null) return NotFound();

        // Validate the language before touching anything: a blank value clears the preference (null →
        // default English), any non-supported code is a 400 so an unknown locale never reaches the DB.
        if (!SupportedLanguages.TryNormalize(req.PreferredLanguage, out var language))
            return Coded(StatusCodes.Status400BadRequest, "invalid_language",
                "That isn't a supported language.");

        // Full replace of the profile fields, not a partial PATCH: a missing field and an explicitly
        // cleared field both arrive as null, so every call must send the whole set. A blank value
        // clears the stored value by design. Don't "fix" a caller into sending only one field — that
        // would silently wipe the others.
        user.FirstName = Normalize(req.FirstName);
        user.LastName = Normalize(req.LastName);
        user.PreferredLanguage = language;
        await db.SaveChangesAsync(ct);

        return await ToProfileAsync(user, ct);
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

    /// <summary>
    /// Starts a change of the account's login email (#27). Re-authenticates with the current password,
    /// validates + de-duplicates the new address, then branches on whether mail is configured:
    /// <b>mail on</b> stages the change and emails a confirmation link to the new address (the change
    /// lands only on confirm, §5.3) — <c>202</c> with the pending address; <b>mail off</b> applies it
    /// immediately and marks the account unverified (no channel to prove the new inbox) — <c>200</c> with
    /// the updated profile. Rate-limited per user. See docs/feature-27-change-email.md §5.1.
    /// </summary>
    [HttpPost("email")]
    [EnableRateLimiting(RateLimiterPolicies.ChangeEmail)]
    [ProducesResponseType<ProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<EmailChangePendingResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ChangeEmail(ChangeEmailRequest req, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([User.UserId()], ct);
        if (user is null) return NotFound();

        // Re-authenticate, exactly like change-password: a stolen session alone can't move the email,
        // which is what closes the change-email→reset takeover at step one (§1).
        if (user.PasswordHash is null || !BCrypt.Net.BCrypt.Verify(req.CurrentPassword, user.PasswordHash))
            return Problem("Your current password is incorrect.", statusCode: StatusCodes.Status400BadRequest);

        var newEmail = (req.NewEmail ?? string.Empty).Trim().ToLowerInvariant();

        if (EmailPolicy.Validate(newEmail) is { } emailError)
            return BadRequest(FieldError("newEmail", emailError));
        if (newEmail == user.Email)
            return Coded(StatusCodes.Status400BadRequest, "email_unchanged", "That's already your email.");
        if (await db.Users.AnyAsync(u => u.Email == newEmail && u.Id != user.Id, ct))
            return Coded(StatusCodes.Status409Conflict, "email_in_use", "That email is already in use.");

        // The AnyAsync check above isn't atomic with the write below, so a concurrent
        // registration/change can still take newEmail first; the unique Users.Email index then rejects
        // our write. Each branch runs in a transaction and maps that DbUpdateException to the documented
        // 409 (never a raw 500), the same guard the confirm path uses. The transaction also keeps the
        // supersede-delete and the write atomic, so a failed write can't leave a prior pending link
        // dropped with nothing applied.

        // Mail off → apply immediately, marked unverified (there is no channel to prove the new inbox).
        if (!await emailSettings.IsEnabledAsync(ct))
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            user.Email = newEmail;
            user.EmailVerified = false;
            await emailChanges.RemoveExistingAsync(user.Id, ct); // any prior pending confirm is moot
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                return Coded(StatusCodes.Status409Conflict, "email_in_use", "That email is already in use.");
            }
            await tx.CommitAsync(ct);
            return Ok(await ToProfileAsync(user, ct));
        }

        // Mail on → stage it (verify-before-commit): supersede any prior pending change, mint a token,
        // commit, then send the confirmation to the NEW address off the request path (like the reset
        // send) so the response returns promptly and a transport hiccup is non-fatal (§5.1).
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await emailChanges.RemoveExistingAsync(user.Id, ct);
            var (token, raw) = emailChanges.Build(user.Id, newEmail);
            db.EmailChangeTokens.Add(token);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // A concurrent request from the same account won the one-live-token slot (the partial
                // unique index on EmailChangeTokens.UserId). The rollback preserves that live token.
                return Coded(StatusCodes.Status409Conflict, "email_change_pending",
                    "A change to your email is already pending. Cancel it or use the link we sent, then try again.");
            }
            await tx.CommitAsync(ct);
            DispatchConfirmationEmail(newEmail, raw);
        }
        return Accepted(new EmailChangePendingResponse(newEmail));
    }

    /// <summary>Cancels a pending (unconfirmed) email change, dropping the token so its emailed link
    /// dies. <c>404</c> when nothing is pending. See docs/feature-27-change-email.md §5.4.</summary>
    [HttpDelete("email")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelEmailChange(CancellationToken ct)
    {
        var removed = await db.EmailChangeTokens
            .Where(t => t.UserId == User.UserId() && t.UsedAt == null)
            .ExecuteDeleteAsync(ct);
        return removed > 0 ? NoContent() : NotFound();
    }

    /// <summary>Sends the change-email confirmation on a background task with its own DI scope and
    /// lifetime — never the request's <c>CancellationToken</c>, which ends when the 202 returns.
    /// Fire-and-forget: a failed send is non-fatal (the user can Resend) and only logged.</summary>
    private void DispatchConfirmationEmail(string newEmail, string rawToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<EmailChangeService>();
                await svc.SendConfirmationAsync(newEmail, rawToken, CancellationToken.None);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Background change-email confirmation send failed; the user can resend.");
            }
        });
    }

    /// <summary>Builds the profile view, reading any live (unused, unexpired) pending email change so the
    /// screen can show it. At most one such row exists per user (the one-live index).</summary>
    private async Task<ProfileResponse> ToProfileAsync(Keepr.Api.Domain.User user, CancellationToken ct)
    {
        var pending = await db.EmailChangeTokens
            .Where(t => t.UserId == user.Id && t.UsedAt == null && t.ExpiresAt > clock.GetUtcNow())
            .Select(t => (string?)t.NewEmail)
            .FirstOrDefaultAsync(ct);

        return new ProfileResponse(
            user.Email, user.FirstName, user.LastName, user.Role.ToString(), user.MustChangePassword,
            user.EmailVerified, pending, user.PreferredLanguage);
    }

    private static ValidationProblemDetails FieldError(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] })
        {
            Status = StatusCodes.Status400BadRequest,
            Detail = message
        };

    private ObjectResult Coded(int status, string code, string detail)
    {
        var pd = new ProblemDetails { Status = status, Detail = detail };
        pd.Extensions["code"] = code;
        return StatusCode(status, pd);
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
