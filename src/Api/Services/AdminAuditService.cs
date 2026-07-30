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
}
