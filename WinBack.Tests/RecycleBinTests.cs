using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WinBack.Core.Data;
using WinBack.Core.Models;
using WinBack.Core.Services;
using Xunit;

namespace WinBack.Tests;

// Factory minimaliste — suffisant pour les tests (pas de pooling)
file sealed class TestDbContextFactory(DbContextOptions<WinBackContext> options)
    : IDbContextFactory<WinBackContext>
{
    public WinBackContext CreateDbContext() => new WinBackContext(options);
}

/// <summary>
/// Vérifie la stratégie <see cref="BackupStrategy.RecycleBin"/> : les fichiers supprimés
/// à la source sont déplacés dans une corbeille datée, et cette corbeille est bien purgée
/// selon la rétention — le dossier écrit et le dossier purgé doivent être le même.
/// </summary>
public class RecycleBinTests : IDisposable
{
    private readonly IDbContextFactory<WinBackContext> _dbFactory;
    private readonly BackupEngine _engine;
    private readonly string _dbPath;
    private readonly string _testRoot;
    private readonly string _sourceDir;
    private readonly string _destRoot;

    private const string PairDest = "Docs";

    public RecycleBinTests()
    {
        _dbPath = Path.GetTempFileName();
        File.Delete(_dbPath); // EnsureCreated crée le fichier

        _testRoot  = Path.Combine(Path.GetTempPath(), "WinBackRecycle_" + Guid.NewGuid().ToString("N"));
        _sourceDir = Path.Combine(_testRoot, "source");
        _destRoot  = Path.Combine(_testRoot, "dest");
        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_destRoot);

        var options = new DbContextOptionsBuilder<WinBackContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _dbFactory = new TestDbContextFactory(options);

        using var db = _dbFactory.CreateDbContext();
        db.Initialize();

        _engine = new BackupEngine(_dbFactory, new DiffCalculator(), NullLogger<BackupEngine>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testRoot, recursive: true); } catch { /* best-effort */ }
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best-effort */ }
    }

    private async Task<BackupProfile> CreateProfileAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var profile = new BackupProfile
        {
            Name = "Recycle",
            VolumeGuid = Guid.NewGuid().ToString(),
            Strategy = BackupStrategy.RecycleBin,
            RetentionDays = 30,
            EnableVss = false,
            Pairs = [new BackupPair { SourcePath = _sourceDir, DestRelativePath = PairDest }]
        };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();
        return profile;
    }

    /// <summary>Racine de la corbeille : à l'intérieur du dossier de destination de la paire.</summary>
    private string RecycleRoot => Path.Combine(_destRoot, PairDest, BackupEngine.RecycleFolderName);

    [Fact]
    public async Task DeletedFile_IsMovedIntoPairRecycleBin()
    {
        var profile = await CreateProfileAsync();
        File.WriteAllText(Path.Combine(_sourceDir, "a.txt"), "contenu");

        // 1re sauvegarde : le fichier est copié et enregistré dans les snapshots
        await _engine.RunAsync(profile, _destRoot);
        Assert.True(File.Exists(Path.Combine(_destRoot, PairDest, "a.txt")));

        // Suppression à la source → 2e sauvegarde → déplacement en corbeille
        File.Delete(Path.Combine(_sourceDir, "a.txt"));
        await _engine.RunAsync(profile, _destRoot);

        Assert.False(File.Exists(Path.Combine(_destRoot, PairDest, "a.txt")));

        var dated = Path.Combine(RecycleRoot, DateTime.Now.ToString("yyyy-MM-dd"));
        Assert.True(File.Exists(Path.Combine(dated, "a.txt")),
            "Le fichier supprimé doit être déplacé dans la corbeille datée de la paire.");
    }

    /// <summary>
    /// Régression : la corbeille était écrite dans le dossier de la paire mais purgée à la
    /// racine du disque. Les deux chemins ne coïncidant pas, la rétention n'avait aucun effet.
    /// </summary>
    [Fact]
    public async Task OldRecycleFolder_IsPurgedAfterRetention()
    {
        var profile = await CreateProfileAsync();

        // Corbeille bien plus ancienne que la rétention (30 jours)
        var stale = Path.Combine(RecycleRoot, "2000-01-01");
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "vieux.txt"), "obsolète");

        // Une sauvegarde avec au moins une suppression déclenche la purge
        File.WriteAllText(Path.Combine(_sourceDir, "b.txt"), "contenu");
        await _engine.RunAsync(profile, _destRoot);
        File.Delete(Path.Combine(_sourceDir, "b.txt"));
        await _engine.RunAsync(profile, _destRoot);

        Assert.False(Directory.Exists(stale),
            "Une corbeille antérieure à la rétention doit être purgée.");

        // La corbeille du jour, elle, doit être conservée
        Assert.True(Directory.Exists(Path.Combine(RecycleRoot, DateTime.Now.ToString("yyyy-MM-dd"))));
    }
}
