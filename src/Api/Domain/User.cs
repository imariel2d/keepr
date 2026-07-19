namespace Keepr.Api.Domain;

/// <summary>
/// An account. <see cref="UsedBytes"/> is a maintained running total (fast path for the
/// "space remaining" meter) kept in sync with <see cref="MediaFile"/> rows inside the same
/// transaction that changes their status. See docs/ai-design-decisions.md (D3, D9).
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;

    /// <summary>Total storage the user is allowed. Default 5 GB.</summary>
    public long QuotaBytes { get; set; } = 5L * 1024 * 1024 * 1024;

    /// <summary>Reserved (pending) + confirmed (ready) bytes currently attributed to the user.</summary>
    public long UsedBytes { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<MediaFile> Files { get; set; } = new List<MediaFile>();

    public long RemainingBytes => Math.Max(0, QuotaBytes - UsedBytes);
}
