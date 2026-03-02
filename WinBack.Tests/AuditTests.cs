using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WinBack.Core.Data;
using WinBack.Core.Models;
using WinBack.Core.Services;
using Xunit;

namespace WinBack.Tests;

public class AuditTests : IAsyncDisposable
{
    private readonly IDbContextFactory<WinBackContext> _dbFactory;
    private readonly BackupEngine _engine;
    private readonly string _dbPath;

    public AuditTests()
    {
        _dbPath = Path.GetTempFileName();
        File.Delete(_dbPath); // EnsureCreated crée le fichier

        var options = new DbContextOptionsBuilder<WinBackContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _dbFactory = new PooledDbContextFactory<WinBackContext>(options);

        using var db = _dbFactory.CreateDbContext();
        db.Initialize();

        _engine = new BackupEngine(_dbFactory, new DiffCalculator(), NullLogger<BackupEngine>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        await Task.CompletedTask;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<(BackupProfile profile, BackupPair pair)> CreateProfileWithPairAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var profile = new BackupProfile
        {
            Name = "Test",
            VolumeGuid = Guid.NewGuid().ToString(),
            EnableHashVerification = true
        };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();

        var pair = new BackupPair
        {
            ProfileId = profile.Id,
            SourcePath = Path.GetTempPath(),
            DestRelativePath = "Dest"
        };
        db.Pairs.Add(pair);
        await db.SaveChangesAsync();

        return (profile, pair);
    }

    private async Task AddSnapshotAsync(int profileId, int pairId, string relativePath, string hash)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.Snapshots.Add(new FileSnapshot
        {
            ProfileId = profileId,
            PairId = pairId,
            RelativePath = relativePath,
            Hash = hash,
            Size = 0,
            LastModified = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    // ── Tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Audit_WithNoSnapshots_ReturnsZeroTotal()
    {
        var (profile, _) = await CreateProfileWithPairAsync();
        var result = await _engine.RunAuditAsync(profile, Path.GetTempPath());
        Assert.Equal(0, result.Total);
        Assert.Equal(0, result.Ok);
    }

    [Fact]
    public async Task Audit_WithMatchingFile_ReturnsOk()
    {
        var (profile, pair) = await CreateProfileWithPairAsync();

        // Créer un fichier temporaire et calculer son hash réel
        var destDir = Path.Combine(Path.GetTempPath(), "AuditTest_Ok_" + Guid.NewGuid());
        Directory.CreateDirectory(destDir);
        var fileName = "test.txt";
        var destFile = Path.Combine(destDir, fileName);
        await File.WriteAllTextAsync(destFile, "contenu de test");

        var hash = await DiffCalculator.ComputeHashAsync(destFile, CancellationToken.None);
        await AddSnapshotAsync(profile.Id, pair.Id, fileName, hash);

        // destRoot tel que Path.Combine(destRoot, pair.DestRelativePath, fileName) = destFile
        // pair.DestRelativePath = "Dest", donc on veut destRoot = parent de destDir
        // Mais ici on recrée la structure : destRoot/Dest/fileName
        var destRoot = Path.Combine(Path.GetTempPath(), "AuditRoot_Ok_" + Guid.NewGuid());
        var destWithPair = Path.Combine(destRoot, pair.DestRelativePath);
        Directory.CreateDirectory(destWithPair);
        var finalDest = Path.Combine(destWithPair, fileName);
        File.Copy(destFile, finalDest);

        try
        {
            var result = await _engine.RunAuditAsync(profile, destRoot);
            Assert.Equal(1, result.Total);
            Assert.Equal(1, result.Ok);
            Assert.Equal(0, result.Missing);
            Assert.Equal(0, result.Corrupted);
        }
        finally
        {
            Directory.Delete(destRoot, recursive: true);
            Directory.Delete(destDir, recursive: true);
        }
    }

    [Fact]
    public async Task Audit_WithMissingFile_ReturnsMissing()
    {
        var (profile, pair) = await CreateProfileWithPairAsync();
        await AddSnapshotAsync(profile.Id, pair.Id, "missing.txt", "abc123");

        var destRoot = Path.Combine(Path.GetTempPath(), "AuditRoot_Missing_" + Guid.NewGuid());
        Directory.CreateDirectory(destRoot);
        try
        {
            var result = await _engine.RunAuditAsync(profile, destRoot);
            Assert.Equal(1, result.Total);
            Assert.Equal(0, result.Ok);
            Assert.Equal(1, result.Missing);
            Assert.Equal(0, result.Corrupted);
        }
        finally
        {
            Directory.Delete(destRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Audit_WithCorruptedFile_ReturnsCorrupted()
    {
        var (profile, pair) = await CreateProfileWithPairAsync();
        var fileName = "corrupted.txt";

        // Hash enregistré ≠ hash réel du fichier → corrompu
        await AddSnapshotAsync(profile.Id, pair.Id, fileName, "hash-original-qui-ne-correspond-pas");

        var destRoot = Path.Combine(Path.GetTempPath(), "AuditRoot_Corrupted_" + Guid.NewGuid());
        var destWithPair = Path.Combine(destRoot, pair.DestRelativePath);
        Directory.CreateDirectory(destWithPair);
        await File.WriteAllTextAsync(Path.Combine(destWithPair, fileName), "contenu modifié");

        try
        {
            var result = await _engine.RunAuditAsync(profile, destRoot);
            Assert.Equal(1, result.Total);
            Assert.Equal(0, result.Ok);
            Assert.Equal(0, result.Missing);
            Assert.Equal(1, result.Corrupted);
            Assert.Contains(fileName, result.CorruptedPaths);
        }
        finally
        {
            Directory.Delete(destRoot, recursive: true);
        }
    }
}
