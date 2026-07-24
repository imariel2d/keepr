using Keepr.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Keepr.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
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
            e.Property(x => x.PasswordHash).IsRequired();
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
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenHash).HasMaxLength(32).IsRequired();

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
    }
}
