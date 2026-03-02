using Microsoft.Extensions.Logging;
using WinBack.Core.Models;
using WinBack.Core.Services;

namespace WinBack.App.Services;

/// <summary>
/// Orchestre le cycle de vie complet d'une sauvegarde :
/// détection → confirmation → délai → exécution → notification.
/// </summary>
public class BackupOrchestrator
{
    private readonly ProfileService _profiles;
    private readonly BackupEngine _engine;
    private readonly NotificationService _notifications;
    private readonly ILogger<BackupOrchestrator> _logger;

    // Annulations actives par profil (avec lettre de lecteur pour détecter le retrait du disque)
    private readonly Dictionary<int, (CancellationTokenSource Cts, char DriveLetter)> _activeTasks = new();
    private readonly HashSet<int> _interruptedProfiles = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Handles de pause par profil — partagés avec BackupEngine via BackupEngineOptions.
    // Cycle de vie : créé dans StartBackupAsync, supprimé dans le finally de StartBackupAsync.
    // ManualResetEventSlim.Reset() = met en pause, .Set() = reprend.
    private readonly Dictionary<int, ManualResetEventSlim> _pauseHandles = new();

    /// <summary>
    /// Callback appelé par la couche App pour demander le mot de passe de chiffrement
    /// avant de démarrer une sauvegarde chiffrée.
    /// Retourne la clé AES-256 dérivée (32 octets) si l'utilisateur a saisi un mot de passe,
    /// ou null si l'utilisateur a annulé (la sauvegarde est alors abandonnée).
    /// Doit être assigné par App.xaml.cs après construction de l'orchestrateur.
    /// </summary>
    public Func<BackupProfile, Task<byte[]?>>? RequestEncryptionKeyAsync { get; set; }

    public event EventHandler<BackupStartedEventArgs>? BackupStarted;
    public event EventHandler<BackupCompletedEventArgs>? BackupCompleted;
    public event EventHandler<NewDriveEventArgs>? UnknownDriveInserted;

    public BackupOrchestrator(
        ProfileService profiles,
        BackupEngine engine,
        NotificationService notifications,
        ILogger<BackupOrchestrator> logger)
    {
        _profiles = profiles;
        _engine = engine;
        _notifications = notifications;
        _logger = logger;
    }

    /// <summary>
    /// Appelé par UsbMonitorService lorsqu'un disque est inséré.
    /// Démarre automatiquement toutes les sauvegardes dont AutoStart est activé.
    /// </summary>
    public async Task OnDriveInsertedAsync(DriveDetails drive)
    {
        var profiles = await _profiles.GetAllByVolumeGuidAsync(drive.VolumeGuid);

        if (profiles.Count == 0)
        {
            _logger.LogInformation("Disque inconnu : {Label} ({Guid})", drive.Label, drive.VolumeGuid);
            UnknownDriveInserted?.Invoke(this, new NewDriveEventArgs(drive));
            var settings = await _profiles.GetSettingsAsync();
            if (settings.ShowNotifications)
                _notifications.NotifyNewDrive(drive.Label, drive.DriveLetter + ":\\");
            return;
        }

        var tasks = new List<Task>();
        foreach (var profile in profiles)
        {
            _logger.LogInformation("Profil trouvé : {Profile} pour {Drive}", profile.Name, drive.DriveLetter);
            if (profile.AutoStart)
            {
                tasks.Add(StartBackupAsync(profile, drive));
            }
            else
            {
                BackupStarted?.Invoke(this, new BackupStartedEventArgs(profile, drive, requiresConfirmation: true));
            }
        }
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Appelé par UsbMonitorService lorsqu'un disque est retiré.
    /// Annule toutes les sauvegardes en cours pour ce lecteur et les marque comme interrompues.
    /// </summary>
    public async Task OnDriveRemovedAsync(char driveLetter)
    {
        List<int> profileIds;
        await _lock.WaitAsync();
        try
        {
            profileIds = _activeTasks
                .Where(kvp => kvp.Value.DriveLetter == driveLetter)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var id in profileIds)
            {
                await _activeTasks[id].Cts.CancelAsync();
                _interruptedProfiles.Add(id);
            }
        }
        finally
        {
            _lock.Release();
        }

        if (profileIds.Count > 0)
            _logger.LogInformation("Disque {Drive} retiré — {Count} sauvegarde(s) interrompue(s)",
                driveLetter, profileIds.Count);
    }

    /// <summary>
    /// Démarre une sauvegarde pour un profil et un disque donnés.
    /// Si le profil a le chiffrement activé, appelle <see cref="RequestEncryptionKeyAsync"/>
    /// pour demander le mot de passe à l'utilisateur avant de démarrer.
    /// </summary>
    public async Task StartBackupAsync(BackupProfile profile, DriveDetails drive, bool dryRun = false)
    {
        // Charger les paramètres avant d'entrer dans le verrou
        var settings = await _profiles.GetSettingsAsync();

        // Si le profil est chiffré, demander la clé à l'utilisateur AVANT de prendre le verrou.
        // Si l'utilisateur annule (retourne null), on abandonne silencieusement.
        byte[]? encryptionKey = null;
        if (profile.EnableEncryption && RequestEncryptionKeyAsync != null)
        {
            encryptionKey = await RequestEncryptionKeyAsync(profile);
            if (encryptionKey == null)
            {
                _logger.LogInformation(
                    "Sauvegarde annulée : mot de passe non fourni pour {Profile}", profile.Name);
                return;
            }
        }

        // Le CTS et le handle de pause sont créés ensemble dans le verrou
        // pour éviter les race conditions avec CancelBackupAsync / PauseBackupAsync.
        CancellationTokenSource? cts = null;
        ManualResetEventSlim? pauseHandle = null;

        await _lock.WaitAsync();
        try
        {
            if (_activeTasks.ContainsKey(profile.Id))
            {
                _logger.LogWarning("Sauvegarde déjà en cours pour {Profile}", profile.Name);
                return;
            }

            cts         = new CancellationTokenSource();
            pauseHandle = new ManualResetEventSlim(initialState: true); // initialState=true → non-pausé
            _activeTasks[profile.Id]  = (cts, drive.DriveLetter[0]);
            _pauseHandles[profile.Id] = pauseHandle;
        }
        finally
        {
            _lock.Release();
        }

        var options = new BackupEngineOptions(
            settings.MaxRetryCount, settings.RetryDelayMs, encryptionKey, pauseHandle);

        // Si on arrive ici, cts est forcément non-null (le return ci-dessus l'aurait stoppé).
        try
        {
            if (settings.ShowNotifications)
                _notifications.NotifyDriveDetected(profile.Name);
            BackupStarted?.Invoke(this, new BackupStartedEventArgs(profile, drive, requiresConfirmation: false));

            // Délai configurable avant démarrage (laisser le disque s'initialiser)
            if (profile.InsertionDelaySeconds > 0)
                await Task.Delay(TimeSpan.FromSeconds(profile.InsertionDelaySeconds), cts!.Token);

            var destRoot = drive.DriveLetter + ":\\";
            var progress = new Progress<BackupProgress>(p =>
            {
                BackupStarted?.Invoke(this, new BackupStartedEventArgs(profile, drive,
                    requiresConfirmation: false) { Progress = p });
            });

            var run = await _engine.RunAsync(profile, destRoot, progress, dryRun, cts!.Token, options);

            // Vérifier si l'annulation est due au retrait du disque → statut Interrupted
            if (run.Status == BackupRunStatus.Cancelled)
            {
                bool wasInterrupted;
                await _lock.WaitAsync();
                try { wasInterrupted = _interruptedProfiles.Remove(profile.Id); }
                finally { _lock.Release(); }

                if (wasInterrupted)
                {
                    await _profiles.UpdateRunStatusAsync(run.Id, BackupRunStatus.Interrupted);
                    run.Status = BackupRunStatus.Interrupted;
                    _logger.LogInformation("Sauvegarde interrompue (disque retiré) : {Profile}", profile.Name);
                }
            }

            if (settings.ShowNotifications)
                _notifications.NotifyBackupComplete(run, profile.Name);
            BackupCompleted?.Invoke(this, new BackupCompletedEventArgs(profile, run));
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Sauvegarde annulée par l'utilisateur : {Profile}", profile.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la sauvegarde {Profile}", profile.Name);
            if (settings.ShowNotifications)
                _notifications.NotifyError($"{profile.Name} : {ex.Message}");
        }
        finally
        {
            await _lock.WaitAsync();
            _activeTasks.Remove(profile.Id);
            _interruptedProfiles.Remove(profile.Id);
            _pauseHandles.Remove(profile.Id);
            _lock.Release();
            cts?.Dispose();
            // Si la sauvegarde était en pause lors de l'annulation, libérer le handle
            // pour ne pas bloquer le thread de pool qui attendait dessus.
            pauseHandle?.Set();
            pauseHandle?.Dispose();
        }
    }

    /// <summary>
    /// Annule la sauvegarde en cours pour un profil.
    /// </summary>
    public async Task CancelBackupAsync(int profileId)
    {
        await _lock.WaitAsync();
        try
        {
            if (_activeTasks.TryGetValue(profileId, out var entry))
                await entry.Cts.CancelAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public bool IsBackupRunning(int profileId) => _activeTasks.ContainsKey(profileId);

    /// <summary>
    /// Met en pause la sauvegarde en cours pour le profil indiqué.
    /// Le moteur terminera le fichier en cours avant de se suspendre.
    /// Sans effet si aucune sauvegarde n'est active pour ce profil.
    /// </summary>
    public async Task PauseBackupAsync(int profileId)
    {
        await _lock.WaitAsync();
        try
        {
            if (_pauseHandles.TryGetValue(profileId, out var handle))
                handle.Reset(); // Bloque le moteur au prochain point de vérification
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Reprend la sauvegarde précédemment mise en pause pour le profil indiqué.
    /// Sans effet si le profil n'est pas en pause.
    /// </summary>
    public async Task ResumeBackupAsync(int profileId)
    {
        await _lock.WaitAsync();
        try
        {
            if (_pauseHandles.TryGetValue(profileId, out var handle))
                handle.Set(); // Débloque le moteur
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Vrai si la sauvegarde du profil est actuellement en pause.
    /// </summary>
    public bool IsBackupPaused(int profileId)
        => _pauseHandles.TryGetValue(profileId, out var h) && !h.IsSet;

    /// <summary>
    /// Calcule les changements qu'effectuerait la prochaine sauvegarde du profil
    /// sans écrire ni modifier aucun fichier.
    /// </summary>
    public async Task<BackupPreviewResult> PreviewBackupAsync(
        int profileId, CancellationToken ct = default)
    {
        var profile = await _profiles.GetProfileByIdAsync(profileId)
            ?? throw new InvalidOperationException($"Profil {profileId} introuvable.");
        return await _engine.PreviewAsync(profile, ct);
    }

    public async Task<AuditResult> RunAuditAsync(int profileId, string destRoot, CancellationToken ct = default)
    {
        var profile = await _profiles.GetProfileByIdAsync(profileId)
            ?? throw new InvalidOperationException($"Profil {profileId} introuvable");
        return await _engine.RunAuditAsync(profile, destRoot, ct);
    }
}

// ── Événements ────────────────────────────────────────────────────────────────

public class BackupStartedEventArgs : EventArgs
{
    public BackupProfile Profile { get; }
    public DriveDetails Drive { get; }
    public bool RequiresConfirmation { get; }
    public BackupProgress? Progress { get; set; }

    public BackupStartedEventArgs(BackupProfile profile, DriveDetails drive, bool requiresConfirmation)
    {
        Profile = profile;
        Drive = drive;
        RequiresConfirmation = requiresConfirmation;
    }
}

public class BackupCompletedEventArgs : EventArgs
{
    public BackupProfile Profile { get; }
    public BackupRun Run { get; }

    public BackupCompletedEventArgs(BackupProfile profile, BackupRun run)
    {
        Profile = profile;
        Run = run;
    }
}

public class NewDriveEventArgs : EventArgs
{
    public DriveDetails Drive { get; }
    public NewDriveEventArgs(DriveDetails drive) => Drive = drive;
}
