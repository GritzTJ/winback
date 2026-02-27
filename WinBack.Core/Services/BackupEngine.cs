using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WinBack.Core.Data;
using WinBack.Core.Models;

namespace WinBack.Core.Services;

public record BackupProgress(
    string CurrentFile,
    int FilesProcessed,
    int TotalFiles,
    long BytesTransferred,
    BackupPhase Phase);

public enum BackupPhase { Scanning, Copying, Deleting, Verifying, Done }

/// <summary>
/// Moteur de sauvegarde incrémentielle. Gère le cycle complet :
/// scan diff → copie VSS → suppression → mise à jour des snapshots.
/// </summary>
public class BackupEngine
{
    private readonly IDbContextFactory<WinBackContext> _dbFactory;
    private readonly DiffCalculator _differ;
    private readonly ILogger<BackupEngine> _logger;

    public BackupEngine(
        IDbContextFactory<WinBackContext> dbFactory,
        DiffCalculator differ,
        ILogger<BackupEngine> logger)
    {
        _dbFactory = dbFactory;
        _differ = differ;
        _logger = logger;
    }

    /// <summary>
    /// Exécute la sauvegarde complète pour un profil.
    /// </summary>
    /// <param name="profile">Profil chargé avec ses Pairs.</param>
    /// <param name="destRootPath">Chemin absolu de la racine du disque de destination.</param>
    /// <param name="progress">Rapport de progression.</param>
    /// <param name="dryRun">Si vrai, simule sans écrire aucun fichier.</param>
    /// <param name="ct">Jeton d'annulation.</param>
    public async Task<BackupRun> RunAsync(
        BackupProfile profile,
        string destRootPath,
        IProgress<BackupProgress>? progress = null,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var run = new BackupRun
        {
            ProfileId = profile.Id,
            StartedAt = DateTime.UtcNow,
            Status = BackupRunStatus.Running,
            IsDryRun = dryRun
        };
        db.Runs.Add(run);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Démarrage sauvegarde profil {ProfileName} (DryRun={DryRun})",
            profile.Name, dryRun);

        using var vssManager = profile.EnableVss ? new VssSessionManager() : null;

        try
        {
            foreach (var pair in profile.Pairs.Where(p => p.IsActive))
            {
                ct.ThrowIfCancellationRequested();
                await ProcessPairAsync(profile, pair, destRootPath, run, vssManager,
                    progress, dryRun, db, ct);
            }

            run.Status = run.FilesErrored > 0 ? BackupRunStatus.PartialSuccess : BackupRunStatus.Success;
        }
        catch (OperationCanceledException)
        {
            run.Status = BackupRunStatus.Cancelled;
            _logger.LogWarning("Sauvegarde annulée pour {ProfileName}", profile.Name);
        }
        catch (Exception ex)
        {
            run.Status = BackupRunStatus.Error;
            run.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Erreur lors de la sauvegarde {ProfileName}", profile.Name);
        }
        finally
        {
            run.FinishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            _logger.LogInformation(
                "Sauvegarde terminée : +{Added} ~{Modified} -{Deleted} erreurs={Errors}",
                run.FilesAdded, run.FilesModified, run.FilesDeleted, run.FilesErrored);
        }

        return run;
    }

    private async Task ProcessPairAsync(
        BackupProfile profile,
        BackupPair pair,
        string destRootPath,
        BackupRun run,
        VssSessionManager? vssManager,
        IProgress<BackupProgress>? progress,
        bool dryRun,
        WinBackContext db,
        CancellationToken ct)
    {
        var sourcePath = pair.SourcePath;
        var destPath = Path.Combine(destRootPath, pair.DestRelativePath);

        if (!Directory.Exists(sourcePath))
        {
            _logger.LogWarning("Dossier source introuvable : {SourcePath}", sourcePath);
            return;
        }

        // Charger les snapshots existants pour cette paire
        var snapshots = await db.Snapshots
            .Where(s => s.ProfileId == profile.Id && s.PairId == pair.Id)
            .ToListAsync(ct);

        // Calculer le diff
        progress?.Report(new BackupProgress("Analyse en cours…", 0, 0, run.BytesTransferred, BackupPhase.Scanning));
        var diff = _differ.Compute(sourcePath, snapshots, pair);

        _logger.LogInformation("Diff paire {PairId}: +{A} ~{M} -{D}",
            pair.Id, diff.Added.Count, diff.Modified.Count, diff.Deleted.Count);

        if (!diff.HasChanges && snapshots.Count > 0)
        {
            _logger.LogInformation("Aucun changement détecté pour {SourcePath}", sourcePath);
            return;
        }

        // Préparer VSS si activé (volume racine de la source, ex: "C:\")
        VssSnapshot? vssSnapshot = null;
        string volumeRoot = Path.GetPathRoot(sourcePath) ?? sourcePath;
        if (vssManager != null && (diff.Added.Count + diff.Modified.Count) > 0)
        {
            vssSnapshot = vssManager.GetOrCreate(volumeRoot);
            if (vssSnapshot != null)
                _logger.LogDebug("VSS snapshot créé pour {VolumeRoot}", volumeRoot);
            else
                _logger.LogWarning("VSS indisponible, copie directe utilisée");
        }

        int totalFiles = diff.Added.Count + diff.Modified.Count + diff.Deleted.Count;
        int processed = 0;

        if (!dryRun)
            Directory.CreateDirectory(destPath);

        // --- Copier les fichiers ajoutés et modifiés ---
        foreach (var relativePath in diff.Added.Concat(diff.Modified))
        {
            ct.ThrowIfCancellationRequested();

            var isAdded = diff.Added.Contains(relativePath);
            progress?.Report(new BackupProgress(relativePath, ++processed, totalFiles,
                run.BytesTransferred, BackupPhase.Copying));

            var sourceFile = Path.Combine(sourcePath, relativePath);
            var vssFile = vssSnapshot != null
                ? vssSnapshot.TranslatePath(sourceFile, volumeRoot)
                : sourceFile;
            var destFile = Path.Combine(destPath, relativePath);

            try
            {
                if (!dryRun)
                {
                    var destDir = Path.GetDirectoryName(destFile)!;
                    Directory.CreateDirectory(destDir);
                    await CopyFileAsync(vssFile, destFile, ct);

                    if (profile.EnableHashVerification)
                    {
                        progress?.Report(new BackupProgress(relativePath, processed, totalFiles,
                            run.BytesTransferred, BackupPhase.Verifying));
                        await VerifyIntegrityAsync(sourceFile, destFile, ct);
                    }
                }

                var fileInfo = new FileInfo(sourceFile);
                run.BytesTransferred += fileInfo.Exists ? fileInfo.Length : 0;

                // Mettre à jour le snapshot
                var snap = snapshots.FirstOrDefault(s =>
                    string.Equals(s.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

                if (snap == null)
                {
                    snap = new FileSnapshot
                    {
                        ProfileId = profile.Id,
                        PairId = pair.Id,
                        RelativePath = relativePath
                    };
                    snapshots.Add(snap);
                    db.Snapshots.Add(snap);
                }

                snap.Size = fileInfo.Exists ? fileInfo.Length : 0;
                snap.LastModified = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTime.UtcNow;

                if (isAdded) run.FilesAdded++;
                else run.FilesModified++;

                run.Entries.Add(new BackupRunEntry
                {
                    Action = isAdded ? EntryAction.Added : EntryAction.Modified,
                    RelativePath = relativePath
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur copie {File}", relativePath);
                run.FilesErrored++;
                run.Entries.Add(new BackupRunEntry
                {
                    Action = EntryAction.Error,
                    RelativePath = relativePath,
                    ErrorDetail = ex.Message
                });
            }
        }

        // --- Traiter les suppressions ---
        if (diff.Deleted.Count > 0)
        {
            progress?.Report(new BackupProgress("Traitement des suppressions…",
                processed, totalFiles, run.BytesTransferred, BackupPhase.Deleting));
        }

        foreach (var relativePath in diff.Deleted)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new BackupProgress(relativePath, ++processed, totalFiles,
                run.BytesTransferred, BackupPhase.Deleting));

            var destFile = Path.Combine(destPath, relativePath);

            try
            {
                if (!dryRun && File.Exists(destFile))
                {
                    switch (profile.Strategy)
                    {
                        case BackupStrategy.Mirror:
                            File.Delete(destFile);
                            CleanEmptyDirectories(destPath, Path.GetDirectoryName(destFile)!);
                            break;

                        case BackupStrategy.RecycleBin:
                            await MoveToRecycleBinAsync(destFile, destPath, profile.RetentionDays, ct);
                            break;

                        case BackupStrategy.Additive:
                            // Ne rien faire : conserver le fichier
                            break;
                    }
                }

                // Supprimer du snapshot
                var snap = snapshots.FirstOrDefault(s =>
                    string.Equals(s.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
                if (snap != null)
                {
                    snapshots.Remove(snap);
                    db.Snapshots.Remove(snap);
                }

                run.FilesDeleted++;
                run.Entries.Add(new BackupRunEntry
                {
                    Action = EntryAction.Deleted,
                    RelativePath = relativePath
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur suppression {File}", relativePath);
                run.FilesErrored++;
            }
        }

        // Purger la corbeille de sauvegarde si nécessaire
        if (!dryRun && profile.Strategy == BackupStrategy.RecycleBin)
            PurgeRecycleBin(destRootPath, profile.RetentionDays);

        // Sauvegarder les snapshots périodiquement
        await db.SaveChangesAsync(ct);
    }

    private static async Task CopyFileAsync(string source, string dest, CancellationToken ct)
    {
        const int bufferSize = 1024 * 1024; // 1 MB
        await using var src = new FileStream(source, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize, FileOptions.Asynchronous);
        await src.CopyToAsync(dst, bufferSize, ct);

        // Préserver la date de modification
        File.SetLastWriteTimeUtc(dest, File.GetLastWriteTimeUtc(source));
    }

    private static async Task VerifyIntegrityAsync(string source, string dest, CancellationToken ct)
    {
        var hashSource = await DiffCalculator.ComputeHashAsync(source, ct);
        var hashDest = await DiffCalculator.ComputeHashAsync(dest, ct);
        if (!string.Equals(hashSource, hashDest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Vérification d'intégrité échouée : {Path.GetFileName(source)}");
    }

    private static async Task MoveToRecycleBinAsync(
        string destFile, string destRoot, int retentionDays, CancellationToken ct)
    {
        var recyclePath = Path.Combine(destRoot, ".winback_recycle",
            DateTime.Now.ToString("yyyy-MM-dd"),
            Path.GetRelativePath(destRoot, destFile));

        Directory.CreateDirectory(Path.GetDirectoryName(recyclePath)!);
        await Task.Run(() => File.Move(destFile, recyclePath, overwrite: true), ct);
    }

    private static void PurgeRecycleBin(string destRoot, int retentionDays)
    {
        var recyclePath = Path.Combine(destRoot, ".winback_recycle");
        if (!Directory.Exists(recyclePath)) return;

        var cutoff = DateTime.Now.AddDays(-retentionDays);
        foreach (var dir in Directory.EnumerateDirectories(recyclePath))
        {
            if (DateTime.TryParse(Path.GetFileName(dir), out var date) && date < cutoff)
            {
                try { Directory.Delete(dir, recursive: true); }
                catch { /* Ignorer */ }
            }
        }
    }

    private static void CleanEmptyDirectories(string rootPath, string dirPath)
    {
        while (!string.Equals(dirPath, rootPath, StringComparison.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(dirPath)) break;
            if (Directory.EnumerateFileSystemEntries(dirPath).Any()) break;
            try { Directory.Delete(dirPath); }
            catch { break; }
            dirPath = Path.GetDirectoryName(dirPath)!;
        }
    }
}
