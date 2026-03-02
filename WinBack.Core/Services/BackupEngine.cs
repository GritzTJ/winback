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
/// Options de comportement pour une exécution de sauvegarde.
/// </summary>
/// <param name="MaxRetryCount">Nombre max de tentatives en cas d'erreur de copie (0 = aucun retry).</param>
/// <param name="RetryDelayMs">Délai en ms entre deux tentatives.</param>
/// <param name="EncryptionKey">
/// Clé AES-256 (32 octets) dérivée du mot de passe utilisateur via <see cref="RestoreEngine.DeriveKey"/>.
/// Null si le chiffrement est désactivé pour ce profil.
/// La clé est calculée par la couche App (après saisie du mot de passe) et passée ici :
/// elle n'est jamais stockée en base de données.
/// </param>
/// <param name="PauseHandle">
/// Handle de pause partagé entre l'orchestrateur et le moteur.
/// Initialement mis (<c>IsSet = true</c> = en cours) ; <c>Reset()</c> met en pause la sauvegarde,
/// <c>Set()</c> la reprend. <c>null</c> = pas de pause possible pour cette exécution.
/// </param>
public record BackupEngineOptions(
    int MaxRetryCount = 0,
    int RetryDelayMs = 500,
    byte[]? EncryptionKey = null,
    ManualResetEventSlim? PauseHandle = null,
    IReadOnlyList<string>? GlobalExcludePatterns = null);

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
        CancellationToken ct = default,
        BackupEngineOptions? options = null)
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

        options ??= new BackupEngineOptions();
        using var vssManager = profile.EnableVss ? new VssSessionManager() : null;

        try
        {
            foreach (var pair in profile.Pairs.Where(p => p.IsActive))
            {
                ct.ThrowIfCancellationRequested();
                await ProcessPairAsync(profile, pair, destRootPath, run, vssManager,
                    progress, dryRun, options, db, ct);
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
        BackupEngineOptions options,
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
        var diff = _differ.Compute(sourcePath, snapshots, pair, options.GlobalExcludePatterns);

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

            // Mettre en pause entre deux fichiers si l'utilisateur l'a demandé
            if (options.PauseHandle is { IsSet: false })
                await WaitIfPausedAsync(options.PauseHandle, progress, processed, totalFiles, run, ct);

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
                string? verifiedHash = null;

                if (!dryRun)
                {
                    var destDir = Path.GetDirectoryName(destFile)!;
                    Directory.CreateDirectory(destDir);

                    int attempt = 0;
                    while (true)
                    {
                        try
                        {
                            // Si le profil est chiffré, la clé a été fournie par l'orchestrateur
                            // (saisie du mot de passe lors de la connexion du disque).
                            if (profile.EnableEncryption && options.EncryptionKey != null)
                                await CopyAndEncryptFileAsync(vssFile, destFile, options.EncryptionKey, ct);
                            else
                                await CopyFileAsync(vssFile, destFile, ct);
                            break;
                        }
                        catch (Exception ex) when (attempt < options.MaxRetryCount && !ct.IsCancellationRequested)
                        {
                            attempt++;
                            _logger.LogWarning("Retry {Attempt}/{Max} pour {File} : {Error}",
                                attempt, options.MaxRetryCount, relativePath, ex.Message);
                            await Task.Delay(options.RetryDelayMs, ct);
                        }
                    }

                    // Hash verification : non applicable si le fichier est chiffré (hash ciphertext ≠ hash source)
                    if (profile.EnableHashVerification && !profile.EnableEncryption)
                    {
                        progress?.Report(new BackupProgress(relativePath, processed, totalFiles,
                            run.BytesTransferred, BackupPhase.Verifying));
                        verifiedHash = await VerifyIntegrityAsync(sourceFile, destFile, ct);
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
                if (verifiedHash != null) snap.Hash = verifiedHash;

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

            // Même logique de pause entre deux suppressions
            if (options.PauseHandle is { IsSet: false })
                await WaitIfPausedAsync(options.PauseHandle, progress, processed, totalFiles, run, ct);

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

    private static async Task<string> VerifyIntegrityAsync(string source, string dest, CancellationToken ct)
    {
        var hashSource = await DiffCalculator.ComputeHashAsync(source, ct);
        var hashDest   = await DiffCalculator.ComputeHashAsync(dest, ct);
        if (!string.Equals(hashSource, hashDest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Vérification d'intégrité échouée : {Path.GetFileName(source)}");
        return hashSource;
    }

    private static async Task CopyAndEncryptFileAsync(string source, string dest, byte[] key, CancellationToken ct)
    {
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        await using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None,
            1024 * 1024, FileOptions.Asynchronous);
        await dst.WriteAsync(aes.IV, ct);

        await using var src = new FileStream(source, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var cs = new System.Security.Cryptography.CryptoStream(
            dst, aes.CreateEncryptor(), System.Security.Cryptography.CryptoStreamMode.Write);
        await src.CopyToAsync(cs, ct);
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

    // ── Pause ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Suspend l'exécution du moteur jusqu'à ce que la reprise soit signalée
    /// ou que le jeton d'annulation soit déclenché.
    /// Le rapport "En pause…" est émis avant le blocage pour mettre à jour l'UI.
    /// L'attente est déléguée à un thread de pool pour ne pas bloquer le thread async.
    /// </summary>
    private static async Task WaitIfPausedAsync(
        ManualResetEventSlim pauseHandle,
        IProgress<BackupProgress>? progress,
        int processed, int totalFiles,
        BackupRun run,
        CancellationToken ct)
    {
        // Double-check : évite le Task.Run si la reprise est arrivée entre la vérification
        // et ici (fenêtre de course très courte, mais possible).
        if (pauseHandle.IsSet) return;

        progress?.Report(new BackupProgress(
            "En pause…", processed, totalFiles, run.BytesTransferred, BackupPhase.Copying));

        // ManualResetEventSlim.Wait(CancellationToken) est synchrone → Task.Run pour
        // libérer le thread async pendant l'attente.
        await Task.Run(() => pauseHandle.Wait(ct), ct);
    }

    // ── Prévisualisation ─────────────────────────────────────────────────────

    /// <summary>
    /// Calcule les changements qui seraient effectués lors de la prochaine sauvegarde
    /// sans écrire aucun fichier ni modifier aucun snapshot.
    /// Seules les paires dont le dossier source est accessible sont prises en compte.
    /// </summary>
    /// <returns>
    /// Résultat agrégé sur toutes les paires actives du profil.
    /// <see cref="BackupPreviewResult.PairsChecked"/> indique combien de paires
    /// avaient leur source accessible (0 = disque source déconnecté).
    /// </returns>
    public async Task<BackupPreviewResult> PreviewAsync(
        BackupProfile profile,
        IReadOnlyList<string>? globalExcludePatterns = null,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        int added = 0, modified = 0, deleted = 0, pairsChecked = 0;

        foreach (var pair in profile.Pairs.Where(p => p.IsActive))
        {
            ct.ThrowIfCancellationRequested();

            // Ignorer silencieusement les sources inaccessibles
            if (!Directory.Exists(pair.SourcePath)) continue;
            pairsChecked++;

            var snapshots = await db.Snapshots
                .Where(s => s.ProfileId == profile.Id && s.PairId == pair.Id)
                .ToListAsync(ct);

            var diff = _differ.Compute(pair.SourcePath, snapshots, pair, globalExcludePatterns);
            added    += diff.Added.Count;
            modified += diff.Modified.Count;
            deleted  += diff.Deleted.Count;
        }

        return new BackupPreviewResult(added, modified, deleted, pairsChecked);
    }

    /// <summary>
    /// Vérifie l'intégrité des fichiers sauvegardés en comparant leurs hashs avec les snapshots.
    /// </summary>
    public async Task<AuditResult> RunAuditAsync(
        BackupProfile profile, string destRoot, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var snapshots = await db.Snapshots
            .Include(s => s.Pair)
            .Where(s => s.ProfileId == profile.Id && s.Hash != null)
            .ToListAsync(ct);

        if (snapshots.Count == 0)
            return new AuditResult(0, 0, 0, 0, []);

        int ok = 0, missing = 0, corrupted = 0;
        var corruptedPaths = new List<string>();

        foreach (var snap in snapshots)
        {
            ct.ThrowIfCancellationRequested();
            var destFile = Path.Combine(destRoot, snap.Pair.DestRelativePath, snap.RelativePath);

            if (!File.Exists(destFile)) { missing++; continue; }

            var hash = await DiffCalculator.ComputeHashAsync(destFile, ct);
            if (string.Equals(hash, snap.Hash, StringComparison.OrdinalIgnoreCase))
                ok++;
            else { corrupted++; corruptedPaths.Add(snap.RelativePath); }
        }

        return new AuditResult(snapshots.Count, ok, missing, corrupted, corruptedPaths);
    }
}

public record AuditResult(int Total, int Ok, int Missing, int Corrupted, List<string> CorruptedPaths);

/// <summary>
/// Résultat d'une prévisualisation de sauvegarde.
/// Résume les changements qui seraient appliqués sans exécuter la sauvegarde.
/// </summary>
/// <param name="FilesAdded">Fichiers nouveaux qui seraient copiés.</param>
/// <param name="FilesModified">Fichiers modifiés qui seraient mis à jour.</param>
/// <param name="FilesDeleted">Fichiers supprimés qui seraient traités (miroir/corbeille).</param>
/// <param name="PairsChecked">Nombre de paires dont la source était accessible.</param>
public record BackupPreviewResult(int FilesAdded, int FilesModified, int FilesDeleted, int PairsChecked)
{
    /// <summary>Nombre total de fichiers à traiter.</summary>
    public int Total => FilesAdded + FilesModified + FilesDeleted;

    /// <summary>Vrai s'il y a au moins un changement à effectuer.</summary>
    public bool HasChanges => Total > 0;
}
