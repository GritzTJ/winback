using Microsoft.EntityFrameworkCore;
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
/// Tests de <see cref="ProfileService"/>, centrés sur la préservation des données
/// qu'un appelant ne connaît pas forcément (sel de chiffrement, paires inactives).
/// </summary>
public class ProfileServiceTests : IDisposable
{
    private readonly IDbContextFactory<WinBackContext> _dbFactory;
    private readonly ProfileService _service;
    private readonly string _dbPath;

    public ProfileServiceTests()
    {
        _dbPath = Path.GetTempFileName();
        File.Delete(_dbPath); // EnsureCreated crée le fichier

        var options = new DbContextOptionsBuilder<WinBackContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _dbFactory = new TestDbContextFactory(options);

        using var db = _dbFactory.CreateDbContext();
        db.Initialize();

        _service = new ProfileService(_dbFactory);
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best-effort */ }
    }

    private static BackupProfile NewProfile(string? salt = null) => new()
    {
        Name = "Profil",
        VolumeGuid = "{11111111-2222-3333-4444-555555555555}",
        EnableEncryption = salt != null,
        EncryptionSalt = salt,
        Pairs =
        [
            new BackupPair { SourcePath = @"C:\Docs", DestRelativePath = "Docs" }
        ]
    };

    /// <summary>
    /// Régression : l'éditeur de profil reconstruit un BackupProfile partiel, sans sel.
    /// Écraser le sel existant par null rendrait indéchiffrables toutes les sauvegardes
    /// déjà écrites avec ce profil.
    /// </summary>
    [Fact]
    public async Task UpdateProfile_WithNullSalt_KeepsExistingSalt()
    {
        var salt = Convert.ToBase64String(RestoreEngine.GenerateSalt());
        var created = await _service.CreateProfileAsync(NewProfile(salt));

        var edited = await _service.GetProfileByIdAsync(created.Id);
        Assert.NotNull(edited);
        edited!.Name = "Renommé";
        edited.EncryptionSalt = null; // l'éditeur ne connaît pas le sel

        await _service.UpdateProfileAsync(edited);

        var reloaded = await _service.GetProfileByIdAsync(created.Id);
        Assert.Equal("Renommé", reloaded!.Name);
        Assert.Equal(salt, reloaded.EncryptionSalt);
    }

    [Fact]
    public async Task UpdateProfile_WithNewSalt_OverwritesSalt()
    {
        var created = await _service.CreateProfileAsync(NewProfile(salt: null));
        var newSalt = Convert.ToBase64String(RestoreEngine.GenerateSalt());

        var edited = await _service.GetProfileByIdAsync(created.Id);
        edited!.EncryptionSalt = newSalt;
        await _service.UpdateProfileAsync(edited);

        var reloaded = await _service.GetProfileByIdAsync(created.Id);
        Assert.Equal(newSalt, reloaded!.EncryptionSalt);
    }

    /// <summary>
    /// Les paires inactives doivent survivre à une mise à jour qui les renvoie :
    /// UpdateProfileAsync supprime uniquement celles qui sont absentes.
    /// </summary>
    [Fact]
    public async Task UpdateProfile_KeepsInactivePairsThatAreResubmitted()
    {
        var profile = NewProfile();
        profile.Pairs.Add(new BackupPair
        {
            SourcePath = @"C:\Photos", DestRelativePath = "Photos", IsActive = false
        });
        var created = await _service.CreateProfileAsync(profile);

        var edited = await _service.GetProfileByIdAsync(created.Id);
        await _service.UpdateProfileAsync(edited!);

        var reloaded = await _service.GetProfileByIdAsync(created.Id);
        Assert.Equal(2, reloaded!.Pairs.Count);
        Assert.Contains(reloaded.Pairs, p => !p.IsActive && p.DestRelativePath == "Photos");
    }

    // ── Export / Import ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportImport_RoundTripsEncryptionSalt()
    {
        var salt = Convert.ToBase64String(RestoreEngine.GenerateSalt());
        var created = await _service.CreateProfileAsync(NewProfile(salt));

        var json = await _service.ExportProfileAsync(created.Id);
        var imported = await _service.ImportProfileAsync(json);

        Assert.NotEqual(created.Id, imported.Id);
        Assert.Equal(salt, imported.EncryptionSalt);
        Assert.True(imported.EnableEncryption);
    }

    /// <summary>
    /// Les fichiers exportés par les versions ≤ 0.4.7 ne contiennent pas du tout
    /// le champ de sel : l'import doit rester possible.
    /// </summary>
    [Fact]
    public async Task Import_WithSaltFieldAbsent_IsAccepted()
    {
        var created = await _service.CreateProfileAsync(NewProfile(salt: null));
        var json = await _service.ExportProfileAsync(created.Id);

        // Retirer entièrement la propriété pour simuler un export d'une version antérieure
        json = System.Text.RegularExpressions.Regex.Replace(
            json, @",\s*""encryptionSalt"":\s*null", string.Empty);
        Assert.DoesNotContain("encryptionSalt", json);

        var imported = await _service.ImportProfileAsync(json);

        Assert.Null(imported.EncryptionSalt);
    }

    [Fact]
    public async Task Import_WithMalformedSalt_IsRejected()
    {
        var created = await _service.CreateProfileAsync(NewProfile(salt: null));
        var json = await _service.ExportProfileAsync(created.Id);

        // Base64 valide mais de la mauvaise longueur (8 octets au lieu de 32)
        json = json.Replace("\"encryptionSalt\": null",
            $"\"encryptionSalt\": \"{Convert.ToBase64String(new byte[8])}\"");

        await Assert.ThrowsAsync<InvalidDataException>(() => _service.ImportProfileAsync(json));
    }
}
