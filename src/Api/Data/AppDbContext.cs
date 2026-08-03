using Keepr.Api.Domain;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Keepr.Api.Data;

// IDataProtectionKeyContext lets the Data Protection key ring persist to this same Postgres, so
// encrypted email API keys (and auth cookies) survive restarts/redeploys and work across instances.
// See docs/feature-36-email-providers.md §4.
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    /// <summary>
    /// Single source of truth for the DB schema name. Used by the model, the migrations-history
    /// table, and any hand-written SQL (which must schema-qualify explicitly).
    /// </summary>
    public const string Schema = "keepr";

    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<ShareLink> ShareLinks => Set<ShareLink>();
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<AdminActionLog> AdminActionLogs => Set<AdminActionLog>();
    public DbSet<AccountInvite> AccountInvites => Set<AccountInvite>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<EmailSettings> EmailSettings => Set<EmailSettings>();

    /// <summary>The Data Protection key ring (§4). Managed by the framework; we only host the table.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Put every object (tables + __EFMigrationsHistory) in our own schema instead of "public".
        // Managed Postgres (e.g. DO) locks down CREATE on "public", but the DB owner can create
        // its own schema — so migrations succeed without any manual GRANT. Migrations emit
        // CREATE SCHEMA IF NOT EXISTS "keepr" automatically.
        b.HasDefaultSchema(Schema);

        b.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(320).IsRequired();

            // PasswordHash is nullable by virtue of the CLR type (an invited-but-unclaimed account
            // has no password yet — §8.1); nothing to configure here. FirstName/LastName are the
            // optional profile fields (#29), length-capped.
            e.Property(x => x.FirstName).HasMaxLength(100);
            e.Property(x => x.LastName).HasMaxLength(100);

            // Stored as a string like MediaFile.Status, so the column reads 'User'/'Admin' rather
            // than an opaque int. The default backfills every pre-existing row to User — no
            // account is silently promoted by the migration.
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(16)
                .HasDefaultValue(Role.User);
        });

        b.Entity<Session>(e =>
        {
            e.HasKey(x => x.Id);

            // Every authenticated request is this lookup, so it must be a unique index probe.
            // Unique also makes a token collision a database error rather than an ambiguous match.
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenHash).HasMaxLength(32).IsRequired();

            e.Property(x => x.UserAgent).HasMaxLength(512);
            e.Property(x => x.CreatedIp).HasMaxLength(45); // INET6_ADDRSTRLEN

            // Cascade: a deleted user's sessions must not outlive them.
            e.HasOne(x => x.User)
                .WithMany(u => u.Sessions)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Drives the per-user cleanup of dead rows on login (§4.2).
            e.HasIndex(x => new { x.UserId, x.ExpiresAt });
        });

        b.Entity<ShareLink>(e =>
        {
            e.HasKey(x => x.Id);

            // Every public link resolve is this lookup, so it must be a unique index probe; unique
            // also turns a token collision into a database error rather than an ambiguous match.
            e.HasIndex(x => x.Token).IsUnique();
            e.Property(x => x.Token).HasMaxLength(64).IsRequired();

            // Cascade: hard-deleting (purging) a file drops its links. A link to a *trashed* file
            // is caught at resolve time by re-checking the file, not by this FK.
            e.HasOne(x => x.File)
                .WithMany()
                .HasForeignKey(x => x.MediaFileId)
                .OnDelete(DeleteBehavior.Cascade);

            // Listing a file's links and the whole-file "stop sharing" both scan by file.
            e.HasIndex(x => x.MediaFileId);
        });

        b.Entity<MediaFile>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.StorageKey).IsUnique();
            e.HasIndex(x => new { x.OwnerId, x.Status });
            e.Property(x => x.StorageKey).IsRequired();
            // 255, not 1024: the uniqueness index below carries the name, and a wider column can
            // push a composite index entry past Postgres's ~2704-byte tuple limit once multibyte
            // characters are involved. 255 also matches every mainstream filesystem's own limit.
            e.Property(x => x.OriginalName).HasMaxLength(255).IsRequired();
            e.Property(x => x.OriginalNameLower).HasMaxLength(255).IsRequired();
            e.Property(x => x.Status).HasConversion<string>();
            e.HasOne(x => x.Owner)
                .WithMany(u => u.Files)
                .HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Folder)
                .WithMany(f => f.Files)
                .HasForeignKey(x => x.FolderId)
                .OnDelete(DeleteBehavior.Restrict);

            // Folder-scoped listing.
            e.HasIndex(x => new { x.OwnerId, x.FolderId, x.Status });

            // No two *live* files in one folder share a name (case-insensitively).
            //  - NULLS NOT DISTINCT so the rule also holds at the root, where FolderId is null.
            //  - Failed excluded, or an abandoned upload would reserve its filename forever.
            //  - Pending included, so a collision surfaces at init rather than after the user has
            //    waited through an entire upload.
            //  - Trashed excluded, so deleting a file frees its name for reuse.
            e.HasIndex(x => new { x.OwnerId, x.FolderId, x.OriginalNameLower })
                .IsUnique()
                .AreNullsDistinct(false)
                .HasFilter($"\"{nameof(MediaFile.Status)}\" <> 'Failed' AND \"{nameof(MediaFile.DeletedAt)}\" IS NULL");

            // Drives the retention sweeper, which scans by age across all users.
            e.HasIndex(x => x.DeletedAt)
                .HasFilter($"\"{nameof(MediaFile.DeletedAt)}\" IS NOT NULL");

            // Soft delete is invisible by default: every query anywhere in the app sees live rows
            // only, and the three places that need trashed rows opt out with IgnoreQueryFilters().
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        b.Entity<Folder>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(255).IsRequired();
            e.Property(x => x.NameLower).HasMaxLength(255).IsRequired();

            e.HasOne(x => x.Owner)
                .WithMany()
                .HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            // Restrict, not Cascade: letting the database delete a subtree would drop rows while
            // their objects stayed in R2 and their bytes stayed charged to the user's quota.
            // Subtrees are drained by TrashService/TrashPurgeService instead.
            e.HasOne(x => x.Parent)
                .WithMany(f => f.Children)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            // Listing a folder's children — also the index the recursive CTEs walk.
            e.HasIndex(x => new { x.OwnerId, x.ParentId });

            // Sibling names are unique per owner, case-insensitively; see the MediaFile index
            // above for why NULLS NOT DISTINCT and the trashed-row exclusion are both needed.
            e.HasIndex(x => new { x.OwnerId, x.ParentId, x.NameLower })
                .IsUnique()
                .AreNullsDistinct(false)
                .HasFilter($"\"{nameof(Folder.DeletedAt)}\" IS NULL");

            e.HasIndex(x => x.DeletedAt)
                .HasFilter($"\"{nameof(Folder.DeletedAt)}\" IS NOT NULL");

            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        b.Entity<AdminActionLog>(e =>
        {
            e.HasKey(x => x.Id);

            // Deliberately no FK to Users on either actor or target: audit rows outlive the
            // accounts they describe (a kick deletes its target). Emails are denormalized
            // snapshots so the log stands alone. See docs/feature-34-admin-console.md §5.
            e.Property(x => x.ActorEmail).HasMaxLength(320).IsRequired();
            e.Property(x => x.TargetEmail).HasMaxLength(320).IsRequired();
            e.Property(x => x.Action).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Details).HasColumnType("jsonb");

            // The audit view lists a target's history newest-first.
            e.HasIndex(x => new { x.TargetUserId, x.CreatedAt });
        });

        b.Entity<AccountInvite>(e =>
        {
            e.HasKey(x => x.Id);

            // Every claim resolves by this lookup, so it must be a unique index probe; unique also
            // turns a token collision into a database error rather than an ambiguous match. 32 bytes
            // = one SHA-256 digest, like Session.TokenHash.
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenHash).HasMaxLength(32).IsRequired();

            // Cascade: a deleted (or kicked) account's pending invite must not outlive it.
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // At most one *live* (unclaimed) invite per account, enforced by the database — not just
            // by ResendInvite deleting before inserting. Without this, two concurrent resends can
            // each delete then insert, leaving two valid claim links for one account. The filter
            // excludes claimed rows (which we keep) so a claim never conflicts. Also serves the
            // by-user lookups (resend/admin view). See docs/feature-36-account-provisioning.md §8.2.
            e.HasIndex(x => x.UserId)
                .IsUnique()
                .HasFilter($"\"{nameof(AccountInvite.ClaimedAt)}\" IS NULL");
        });

        b.Entity<PasswordResetToken>(e =>
        {
            e.HasKey(x => x.Id);

            // Every reset resolves by this lookup, so it must be a unique index probe; unique also
            // turns a token collision into a database error rather than an ambiguous match. 32 bytes
            // = one SHA-256 digest, like AccountInvite.TokenHash / Session.TokenHash.
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenHash).HasMaxLength(32).IsRequired();

            // Cascade: a deleted (or kicked) account's reset token must not outlive it.
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // At most one *live* (unused) reset token per account, enforced by the database — not
            // just by a repeat request deleting before inserting. The filter excludes used rows so a
            // completed reset never conflicts. Also serves the by-user lookups. Mirrors the
            // AccountInvite one-live-invite index. See docs/feature-26-password-reset.md §4.
            e.HasIndex(x => x.UserId)
                .IsUnique()
                .HasFilter($"\"{nameof(PasswordResetToken.UsedAt)}\" IS NULL");
        });

        b.Entity<EmailSettings>(e =>
        {
            e.HasKey(x => x.Id);

            // Singleton: exactly one row, Id = 1. The CHECK stops a second row from ever existing, so
            // the app can always update-in-place rather than guess which row is "the" config. §3.
            e.ToTable(t => t.HasCheckConstraint("CK_EmailSettings_Singleton", "\"Id\" = 1"));
            e.Property(x => x.Id).ValueGeneratedNever();

            // Stored as a string like Role, so the column reads the enum member name —
            // 'None'/'Resend'/'Brevo'/'Mailgun' (PascalCase) — rather than an opaque int. The API
            // surface lowercases it on the way out and parses case-insensitively in; hand-written SQL
            // must match the stored PascalCase form.
            e.Property(x => x.Provider).HasConversion<string>().HasMaxLength(16)
                .HasDefaultValue(EmailProvider.None);
            e.Property(x => x.FromAddress).HasMaxLength(320);
            e.Property(x => x.FromName).HasMaxLength(200);
            e.Property(x => x.MailgunDomain).HasMaxLength(255);
            e.Property(x => x.MailgunRegion).HasMaxLength(2);
            e.Property(x => x.PublicBaseUrl).HasMaxLength(2048);
            e.Property(x => x.LastTestError).HasMaxLength(2000);

            // Seed the singleton `none` row so there is always exactly one row to update. `none` means
            // "defer to the env SMTP fallback, else send nothing" (§5.1). PublicBaseUrl /
            // InviteExpiryDays are re-seeded from env at startup by EmailSettingsSeeder.
            e.HasData(new EmailSettings
            {
                Id = 1,
                Provider = EmailProvider.None,
                FromName = "Keepr",
                InviteExpiryDays = 7,
                UpdatedAt = DateTimeOffset.UnixEpoch
            });
        });
    }
}
