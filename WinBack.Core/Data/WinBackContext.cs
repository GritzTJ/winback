using Microsoft.EntityFrameworkCore;
using WinBack.Core.Models;

namespace WinBack.Core.Data;

public class WinBackContext : DbContext
{
    public DbSet<BackupProfile> Profiles => Set<BackupProfile>();
    public DbSet<BackupPair> Pairs => Set<BackupPair>();
    public DbSet<FileSnapshot> Snapshots => Set<FileSnapshot>();
    public DbSet<BackupRun> Runs => Set<BackupRun>();
    public DbSet<BackupRunEntry> RunEntries => Set<BackupRunEntry>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();

    public WinBackContext(DbContextOptions<WinBackContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // BackupProfile
        modelBuilder.Entity<BackupProfile>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.VolumeGuid).IsRequired().HasMaxLength(50);
            e.HasIndex(x => x.VolumeGuid);
            e.HasMany(x => x.Pairs).WithOne(x => x.Profile).HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Runs).WithOne(x => x.Profile).HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Snapshots).WithOne(x => x.Profile).HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
        });

        // BackupPair
        modelBuilder.Entity<BackupPair>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.SourcePath).IsRequired().HasMaxLength(500);
            e.Property(x => x.DestRelativePath).IsRequired().HasMaxLength(500);
            e.Ignore(x => x.ExcludePatterns); // propriété calculée
            e.HasMany(x => x.Snapshots).WithOne(x => x.Pair).HasForeignKey(x => x.PairId).OnDelete(DeleteBehavior.Cascade);
        });

        // FileSnapshot : clé primaire composite
        modelBuilder.Entity<FileSnapshot>(e =>
        {
            e.HasKey(x => new { x.ProfileId, x.PairId, x.RelativePath });
            e.Property(x => x.RelativePath).HasMaxLength(1000);
        });

        // BackupRun
        modelBuilder.Entity<BackupRun>(e =>
        {
            e.HasKey(x => x.Id);
            e.Ignore(x => x.Duration);
            e.Ignore(x => x.TotalFiles);
            e.HasMany(x => x.Entries).WithOne(x => x.Run).HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        });

        // BackupRunEntry
        modelBuilder.Entity<BackupRunEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.RelativePath).HasMaxLength(1000);
        });

        // AppSettings singleton
        modelBuilder.Entity<AppSettings>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasData(new AppSettings { Id = 1 });
        });
    }

    /// <summary>
    /// Retourne (ou crée) le chemin vers la base SQLite dans AppData\Local\WinBack.
    /// </summary>
    public static string GetDatabasePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinBack");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "winback.db");
    }

    /// <summary>
    /// Initialise la base de données (création + migrations).
    /// </summary>
    public void Initialize()
    {
        Database.EnsureCreated();
    }
}
