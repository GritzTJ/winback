using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WinBack.Core.Data;
using WinBack.Core.Models;

namespace WinBack.Core.Services;

/// <summary>
/// CRUD pour les profils de sauvegarde et les paramètres de l'application.
/// </summary>
public class ProfileService
{
    private readonly IDbContextFactory<WinBackContext> _dbFactory;

    public ProfileService(IDbContextFactory<WinBackContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // ── Profils ──────────────────────────────────────────────────────────────

    public async Task<List<BackupProfile>> GetAllProfilesAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Profiles
            .Include(p => p.Pairs)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<BackupProfile?> GetProfileByIdAsync(int profileId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Profiles.Include(p => p.Pairs).FirstOrDefaultAsync(p => p.Id == profileId);
    }

    public async Task<BackupProfile?> GetByVolumeGuidAsync(string volumeGuid)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Profiles
            .Include(p => p.Pairs)
            .FirstOrDefaultAsync(p => p.VolumeGuid == volumeGuid && p.IsActive);
    }

    public async Task<List<BackupProfile>> GetAllByVolumeGuidAsync(string volumeGuid)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Profiles
            .Include(p => p.Pairs)
            .Where(p => p.VolumeGuid == volumeGuid && p.IsActive)
            .ToListAsync();
    }

    public async Task<BackupProfile> CreateProfileAsync(BackupProfile profile)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        profile.CreatedAt = DateTime.UtcNow;
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();
        return profile;
    }

    public async Task UpdateProfileAsync(BackupProfile profile)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.Profiles.Update(profile);
        await db.SaveChangesAsync();
    }

    public async Task DeleteProfileAsync(int profileId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var profile = await db.Profiles.FindAsync(profileId);
        if (profile != null)
        {
            db.Profiles.Remove(profile);
            await db.SaveChangesAsync();
        }
    }

    // ── Paires source/destination ─────────────────────────────────────────────

    public async Task AddPairAsync(BackupPair pair)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.Pairs.Add(pair);
        await db.SaveChangesAsync();
    }

    public async Task UpdatePairAsync(BackupPair pair)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.Pairs.Update(pair);
        await db.SaveChangesAsync();
    }

    public async Task DeletePairAsync(int pairId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var pair = await db.Pairs.FindAsync(pairId);
        if (pair != null)
        {
            db.Pairs.Remove(pair);
            await db.SaveChangesAsync();
        }
    }

    public async Task UpdateRunStatusAsync(int runId, BackupRunStatus status)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var run = await db.Runs.FindAsync(runId);
        if (run != null) { run.Status = status; await db.SaveChangesAsync(); }
    }

    // ── Historique ───────────────────────────────────────────────────────────

    public async Task<List<BackupRun>> GetRecentRunsAsync(int count = 50)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Runs
            .Include(r => r.Profile)
            .OrderByDescending(r => r.StartedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<List<BackupRun>> GetRunsForProfileAsync(int profileId, int count = 20)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Runs
            .Where(r => r.ProfileId == profileId)
            .OrderByDescending(r => r.StartedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<List<BackupRunEntry>> GetRunEntriesAsync(int runId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.RunEntries
            .Where(e => e.RunId == runId)
            .OrderBy(e => e.Action)
            .ThenBy(e => e.RelativePath)
            .ToListAsync();
    }

    // ── Paramètres ───────────────────────────────────────────────────────────

    public async Task<AppSettings> GetSettingsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Settings.FindAsync(1) ?? new AppSettings();
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        settings.Id = 1;
        db.Settings.Update(settings);
        await db.SaveChangesAsync();
    }

    // ── Export / Import de profils ────────────────────────────────────────────

    // Options JSON communes : camelCase, indenté pour la lisibilité
    private static readonly JsonSerializerOptions _jsonExportOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions _jsonImportOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Sérialise un profil (avec ses paires) au format JSON WinBack.
    /// Le JSON résultant peut être sauvegardé dans un fichier <c>.winback.json</c>
    /// et réimporté sur n'importe quelle machine via <see cref="ImportProfileAsync"/>.
    /// Le mot de passe de chiffrement n'est jamais exporté.
    /// </summary>
    /// <param name="profileId">Identifiant du profil à exporter.</param>
    /// <returns>Chaîne JSON indenté représentant le profil.</returns>
    public async Task<string> ExportProfileAsync(int profileId)
    {
        var profile = await GetProfileByIdAsync(profileId)
            ?? throw new InvalidOperationException($"Profil {profileId} introuvable.");

        var dto = new ProfileExportDto(
            FormatVersion: "1",
            Name: profile.Name,
            VolumeGuid: profile.VolumeGuid,
            DiskLabel: profile.DiskLabel,
            Strategy: profile.Strategy.ToString(),
            RetentionDays: profile.RetentionDays,
            InsertionDelaySeconds: profile.InsertionDelaySeconds,
            EnableHashVerification: profile.EnableHashVerification,
            EnableVss: profile.EnableVss,
            AutoStart: profile.AutoStart,
            EnableEncryption: profile.EnableEncryption,
            Pairs: profile.Pairs.Select(p => new PairExportDto(
                SourcePath: p.SourcePath,
                DestRelativePath: p.DestRelativePath,
                ExcludePatternsJson: p.ExcludePatternsJson,
                IsActive: p.IsActive)).ToList());

        return JsonSerializer.Serialize(dto, _jsonExportOptions);
    }

    /// <summary>
    /// Crée un nouveau profil à partir d'un JSON exporté par <see cref="ExportProfileAsync"/>.
    /// Un identifiant neuf est attribué au profil importé (pas de collision avec les profils existants).
    /// Note : si le chiffrement était activé sur le profil exporté, il sera activé ici aussi,
    /// mais le mot de passe devra être ressaisi lors de la prochaine sauvegarde.
    /// </summary>
    /// <param name="json">Contenu JSON d'un fichier <c>.winback.json</c>.</param>
    /// <returns>Le profil créé en base de données.</returns>
    /// <exception cref="InvalidDataException">Si le JSON est invalide ou la version non supportée.</exception>
    public async Task<BackupProfile> ImportProfileAsync(string json)
    {
        ProfileExportDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<ProfileExportDto>(json, _jsonImportOptions)
                ?? throw new InvalidDataException("Le fichier ne contient pas de profil valide.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Fichier JSON invalide : {ex.Message}", ex);
        }

        if (dto.FormatVersion != "1")
            throw new InvalidDataException(
                $"Version de format non supportée : {dto.FormatVersion}. " +
                "Mettez à jour WinBack pour importer ce fichier.");

        if (!Enum.TryParse<BackupStrategy>(dto.Strategy, ignoreCase: true, out var strategy))
            throw new InvalidDataException($"Stratégie inconnue : {dto.Strategy}");

        // Validation des champs importés
        if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Length > 200)
            throw new InvalidDataException("Nom du profil invalide (vide ou > 200 caractères).");
        if (string.IsNullOrWhiteSpace(dto.VolumeGuid) || dto.VolumeGuid.Length > 50)
            throw new InvalidDataException("GUID de volume invalide.");
        if (dto.RetentionDays < 0 || dto.RetentionDays > 3650)
            throw new InvalidDataException($"Nombre de jours de rétention invalide : {dto.RetentionDays}");
        if (dto.InsertionDelaySeconds < 0 || dto.InsertionDelaySeconds > 300)
            throw new InvalidDataException($"Délai d'insertion invalide : {dto.InsertionDelaySeconds}");
        if (dto.Pairs.Count > 100)
            throw new InvalidDataException($"Trop de paires source/destination : {dto.Pairs.Count} (max 100).");

        foreach (var p in dto.Pairs)
        {
            if (string.IsNullOrWhiteSpace(p.SourcePath))
                throw new InvalidDataException("Chemin source vide dans une paire.");
            // Refuser les chemins UNC (\\serveur\...) dans SourcePath pour empêcher
            // une exfiltration de données vers un partage réseau contrôlé par l'attaquant.
            if (p.SourcePath.TrimStart().StartsWith(@"\\"))
                throw new InvalidDataException($"Chemin source UNC interdit : {p.SourcePath}");
            // Refuser les chemins relatifs ou avec des séquences ".." dans DestRelativePath
            if (!string.IsNullOrWhiteSpace(p.DestRelativePath) && p.DestRelativePath.Contains(".."))
                throw new InvalidDataException($"Chemin de destination invalide (path traversal) : {p.DestRelativePath}");
        }

        var profile = new BackupProfile
        {
            Name           = dto.Name.Trim(),
            VolumeGuid     = dto.VolumeGuid.Trim(),
            DiskLabel      = dto.DiskLabel,
            Strategy       = strategy,
            RetentionDays  = dto.RetentionDays,
            InsertionDelaySeconds  = dto.InsertionDelaySeconds,
            EnableHashVerification = dto.EnableHashVerification,
            EnableVss      = dto.EnableVss,
            AutoStart      = dto.AutoStart,
            EnableEncryption = dto.EnableEncryption,
            Pairs = dto.Pairs.Select(p => new BackupPair
            {
                SourcePath         = p.SourcePath.Trim(),
                DestRelativePath   = p.DestRelativePath.Trim(),
                ExcludePatternsJson = p.ExcludePatternsJson,
                IsActive           = p.IsActive
            }).ToList()
        };

        return await CreateProfileAsync(profile);
    }
}
