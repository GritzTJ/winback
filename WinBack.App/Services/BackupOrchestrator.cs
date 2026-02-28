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

    // Annulations actives par profil
    private readonly Dictionary<int, CancellationTokenSource> _activeTasks = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

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
    /// </summary>
    public async Task OnDriveInsertedAsync(DriveDetails drive)
    {
        var profile = await _profiles.GetByVolumeGuidAsync(drive.VolumeGuid);

        if (profile == null)
        {
            _logger.LogInformation("Disque inconnu : {Label} ({Guid})", drive.Label, drive.VolumeGuid);
            UnknownDriveInserted?.Invoke(this, new NewDriveEventArgs(drive));
            _notifications.NotifyNewDrive(drive.Label, drive.DriveLetter + ":\\");
            return;
        }

        _logger.LogInformation("Profil trouvé : {Profile} pour {Drive}", profile.Name, drive.DriveLetter);

        if (profile.AutoStart)
        {
            await StartBackupAsync(profile, drive);
        }
        else
        {
            // Demander confirmation via événement
            BackupStarted?.Invoke(this, new BackupStartedEventArgs(profile, drive, requiresConfirmation: true));
        }
    }

    /// <summary>
    /// Démarre une sauvegarde pour un profil et un disque donnés.
    /// </summary>
    public async Task StartBackupAsync(BackupProfile profile, DriveDetails drive, bool dryRun = false)
    {
        // Le CTS est capturé dans une variable locale avant de relâcher le verrou
        // pour éviter la race condition avec CancelBackupAsync.
        CancellationTokenSource? cts = null;

        await _lock.WaitAsync();
        try
        {
            if (_activeTasks.ContainsKey(profile.Id))
            {
                _logger.LogWarning("Sauvegarde déjà en cours pour {Profile}", profile.Name);
                return;
            }

            cts = new CancellationTokenSource();
            _activeTasks[profile.Id] = cts;
        }
        finally
        {
            _lock.Release();
        }

        // Si on arrive ici, cts est forcément non-null (le return ci-dessus l'aurait stoppé).
        try
        {
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

            var run = await _engine.RunAsync(profile, destRoot, progress, dryRun, cts!.Token);

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
            _notifications.NotifyError($"{profile.Name} : {ex.Message}");
        }
        finally
        {
            await _lock.WaitAsync();
            _activeTasks.Remove(profile.Id);
            _lock.Release();
            cts?.Dispose();
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
            if (_activeTasks.TryGetValue(profileId, out var cts))
                await cts.CancelAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public bool IsBackupRunning(int profileId)
    {
        return _activeTasks.ContainsKey(profileId);
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
