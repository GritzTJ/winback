using Microsoft.EntityFrameworkCore;
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

    public async Task<BackupProfile?> GetByVolumeGuidAsync(string volumeGuid)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Profiles
            .Include(p => p.Pairs)
            .FirstOrDefaultAsync(p => p.VolumeGuid == volumeGuid && p.IsActive);
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
}
