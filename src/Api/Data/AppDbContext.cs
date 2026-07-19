using Keepr.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Keepr.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(320).IsRequired();
            e.Property(x => x.PasswordHash).IsRequired();
        });

        b.Entity<MediaFile>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.StorageKey).IsUnique();
            e.HasIndex(x => new { x.OwnerId, x.Status });
            e.Property(x => x.StorageKey).IsRequired();
            e.Property(x => x.OriginalName).HasMaxLength(1024).IsRequired();
            e.Property(x => x.Status).HasConversion<string>();
            e.HasOne(x => x.Owner)
                .WithMany(u => u.Files)
                .HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
