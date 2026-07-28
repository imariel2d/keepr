namespace Keepr.Api.Domain;

/// <summary>
/// What an account may do. Two flat roles is the whole authorization model — Keepr is a small
/// private deployment with no per-resource permissions. Stored as a string (see AppDbContext),
/// like <see cref="MediaStatus"/>, so the DB is self-describing. See docs/admin-console-design.md.
/// </summary>
public enum Role
{
    /// <summary>A regular account: owns its own files, sees only its own data.</summary>
    User = 0,

    /// <summary>May administer other accounts — list them, adjust quota, remove them.</summary>
    Admin = 1
}
