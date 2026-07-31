namespace Keepr.Api.Domain;

/// <summary>What an admin did. Stored as a string (see AppDbContext), so the log reads plainly.</summary>
public enum AdminActionType
{
    /// <summary>An admin changed a user's storage quota.</summary>
    QuotaChanged = 0,

    /// <summary>An admin removed a user — access revoked and all their files wiped.</summary>
    UserKicked = 1,

    /// <summary>An admin provisioned a new account (with a password or an email invite).</summary>
    UserCreated = 2,

    /// <summary>An admin changed a user's role (promote/demote).</summary>
    RoleChanged = 3
}

/// <summary>
/// One audited admin action. Deliberately has <b>no foreign keys to <see cref="User"/></b>: an
/// audit row must outlive the account it describes — the whole point of logging a kick is that the
/// record survives the target's deletion. Actor and target email are therefore denormalized
/// snapshots, so the log is readable on its own. See docs/feature-34-admin-console.md §5 and Q-A2.
/// </summary>
public class AdminActionLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The admin who acted.</summary>
    public Guid ActorUserId { get; set; }

    /// <summary>The acting admin's email at the time — readable even if that admin is later removed.</summary>
    public string ActorEmail { get; set; } = default!;

    public AdminActionType Action { get; set; }

    /// <summary>The affected account. No FK — the target may be deleted (a kick).</summary>
    public Guid TargetUserId { get; set; }

    /// <summary>The affected account's email at the time — the record that outlives the deletion.</summary>
    public string TargetEmail { get; set; } = default!;

    /// <summary>Action-specific JSON (jsonb), e.g. <c>{"from":5368709120,"to":10737418240}</c> for a
    /// quota change. Null when the action needs no extra detail.</summary>
    public string? Details { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
