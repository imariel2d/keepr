using System.Text.Json;
using Keepr.Api.Data;
using Keepr.Api.Domain;

namespace Keepr.Api.Services;

/// <summary>
/// Records admin actions to <see cref="AdminActionLog"/>. Each method only <em>adds</em> the row to
/// the context — it does not save. The caller commits it in the <b>same</b> <c>SaveChangesAsync</c>
/// as the state change it records, so an action and its audit entry are all-or-nothing (a failed
/// quota update leaves no audit row claiming it happened). See docs/feature-34-admin-console.md §5.
/// </summary>
public class AdminAuditService(AppDbContext db)
{
    public void RecordQuotaChange(
        Guid actorId, string actorEmail, User target, long fromBytes, long toBytes)
    {
        db.AdminActionLogs.Add(new AdminActionLog
        {
            ActorUserId = actorId,
            ActorEmail = actorEmail,
            Action = AdminActionType.QuotaChanged,
            TargetUserId = target.Id,
            TargetEmail = target.Email,
            Details = JsonSerializer.Serialize(new { from = fromBytes, to = toBytes })
        });
    }

    public void RecordUserKicked(
        Guid actorId, string actorEmail, Guid targetId, string targetEmail)
    {
        db.AdminActionLogs.Add(new AdminActionLog
        {
            ActorUserId = actorId,
            ActorEmail = actorEmail,
            Action = AdminActionType.UserKicked,
            TargetUserId = targetId,
            TargetEmail = targetEmail
        });
    }

    public void RecordUserCreated(
        Guid actorId, string actorEmail, User target, bool invited)
    {
        db.AdminActionLogs.Add(new AdminActionLog
        {
            ActorUserId = actorId,
            ActorEmail = actorEmail,
            Action = AdminActionType.UserCreated,
            TargetUserId = target.Id,
            TargetEmail = target.Email,
            // "invited" (email claim) vs "direct" (admin set the password) — the audit records how.
            Details = JsonSerializer.Serialize(new { role = target.Role.ToString(), invited })
        });
    }

    public void RecordRoleChange(
        Guid actorId, string actorEmail, User target, Role from, Role to)
    {
        db.AdminActionLogs.Add(new AdminActionLog
        {
            ActorUserId = actorId,
            ActorEmail = actorEmail,
            Action = AdminActionType.RoleChanged,
            TargetUserId = target.Id,
            TargetEmail = target.Email,
            Details = JsonSerializer.Serialize(new { from = from.ToString(), to = to.ToString() })
        });
    }

    /// <summary>
    /// Records a change to the app-wide email provider settings. There is no target user, so the
    /// acting admin is recorded as the target (self). The audit detail is a <b>secret-free
    /// allowlist</b> — provider, From, Mailgun domain/region, the link/expiry fields, and whether the
    /// key changed — never the API key, ciphertext, request DTO, or raw provider error text. See
    /// docs/feature-36-email-providers.md §6.
    /// </summary>
    public void RecordEmailSettingsChanged(
        Guid actorId, string actorEmail, EmailSettings settings, bool keyChanged)
    {
        db.AdminActionLogs.Add(new AdminActionLog
        {
            ActorUserId = actorId,
            ActorEmail = actorEmail,
            Action = AdminActionType.EmailSettingsChanged,
            TargetUserId = actorId,
            TargetEmail = actorEmail,
            Details = JsonSerializer.Serialize(new
            {
                provider = settings.Provider.ToString(),
                fromAddress = settings.FromAddress,
                fromName = settings.FromName,
                mailgunDomain = settings.MailgunDomain,
                mailgunRegion = settings.MailgunRegion,
                publicBaseUrl = settings.PublicBaseUrl,
                inviteExpiryDays = settings.InviteExpiryDays,
                keyChanged
            })
        });
    }
}
