using Keepr.Api.Data;
using Keepr.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Keepr.Api.Features.Auth;

/// <summary>
/// The bootstrap admin's credentials, read from configuration (env <c>Admin__Email</c> /
/// <c>Admin__Password</c>). Both blank is the safe default — no admin is created. The password is
/// an <em>initial</em> secret only: once the account exists the DB hash is the source of truth,
/// and editing this value later does nothing (that would be reset-password, #28). See
/// docs/feature-34-admin-console.md §3 and Q-A1.
/// </summary>
public class AdminOptions
{
    public const string SectionName = "Admin";

    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}

/// <summary>
/// Ensures a first admin exists, so the admin console is reachable on a fresh deployment where
/// registration is invite-gated and no account can grant itself the role. Runs once at startup,
/// after migrations. Idempotent:
///
/// <list type="bullet">
///   <item>no such account → create it as <see cref="Role.Admin"/>;</item>
///   <item>account exists but isn't admin → promote it (password untouched);</item>
///   <item>account exists and is admin → nothing;</item>
///   <item>env unset → nothing, but warn if the instance has no admin at all.</item>
/// </list>
///
/// It never resets an existing account's password — promotion is a role change only. See Q-A1.
/// </summary>
public class AdminSeeder(
    AppDbContext db,
    IOptions<AdminOptions> options,
    IOptions<QuotaOptions> quota,
    ILogger<AdminSeeder> log)
{
    public async Task EnsureSeededAsync(CancellationToken ct = default)
    {
        var email = options.Value.Email?.Trim().ToLowerInvariant();
        var password = options.Value.Password;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            // Fail-safe, like the registration gate's "no code configured": do nothing rather than
            // invent an admin, but make the reachability gap loud if there is genuinely no admin.
            if (!await db.Users.AnyAsync(u => u.Role == Role.Admin, ct))
                log.LogWarning(
                    "No admin account exists and Admin__Email/Admin__Password are unset — the admin "
                    + "console is unreachable. Set both to bootstrap the first admin.");
            return;
        }

        var existing = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);

        if (existing is null)
        {
            db.Users.Add(new User
            {
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                Role = Role.Admin,
                QuotaBytes = quota.Value.DefaultBytes
            });
            await db.SaveChangesAsync(ct);
            log.LogInformation(
                "Bootstrap admin {Email} created. Change this password after first sign-in — "
                + "Admin__Password is an initial secret only.", email);
        }
        else if (existing.Role != Role.Admin)
        {
            existing.Role = Role.Admin;
            await db.SaveChangesAsync(ct);
            log.LogInformation(
                "Existing account {Email} promoted to admin (password unchanged).", email);
        }
        else
        {
            log.LogDebug("Bootstrap admin {Email} already present; nothing to do.", email);
        }
    }
}
