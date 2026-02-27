using H.NotifyIcon;
using WinBack.Core.Models;

namespace WinBack.App.Services;

/// <summary>
/// Gère les notifications utilisateur via l'icône de la barre système.
/// Utilise les balloon tips natifs Windows (compatibles sans packaging MSIX).
/// </summary>
public class NotificationService
{
    private TaskbarIcon? _trayIcon;

    public void Initialize(TaskbarIcon trayIcon)
    {
        _trayIcon = trayIcon;
    }

    public void NotifyDriveDetected(string profileName)
    {
        ShowBalloon("WinBack — Disque détecté",
            $"Sauvegarde de « {profileName} » en cours…",
            BalloonIcon.Info);
    }

    public void NotifyBackupComplete(BackupRun run, string profileName)
    {
        if (run.Status == BackupRunStatus.Success || run.Status == BackupRunStatus.PartialSuccess)
        {
            var duration = run.Duration.HasValue
                ? FormatDuration(run.Duration.Value)
                : "";

            var msg = run.TotalFiles == 0
                ? "Aucun changement détecté."
                : $"+{run.FilesAdded} ajouté(s)  ~{run.FilesModified} modifié(s)  -{run.FilesDeleted} supprimé(s)\n{FormatBytes(run.BytesTransferred)}{(duration.Length > 0 ? $" en {duration}" : "")}";

            var icon = run.FilesErrored > 0 ? BalloonIcon.Warning : BalloonIcon.Info;
            ShowBalloon($"WinBack — {profileName}", msg, icon);
        }
        else if (run.Status == BackupRunStatus.Error)
        {
            ShowBalloon($"WinBack — Erreur ({profileName})",
                run.ErrorMessage ?? "Une erreur est survenue.",
                BalloonIcon.Error);
        }
    }

    public void NotifyNewDrive(string driveLabel, string drivePath)
    {
        ShowBalloon("WinBack — Nouveau disque",
            $"Disque « {driveLabel} » ({drivePath}) détecté.\nCliquez pour configurer une sauvegarde.",
            BalloonIcon.Info);
    }

    public void NotifyError(string message)
    {
        ShowBalloon("WinBack — Erreur", message, BalloonIcon.Error);
    }

    private void ShowBalloon(string title, string message, BalloonIcon icon)
    {
        if (_trayIcon == null) return;
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            _trayIcon.ShowBalloonTip(title, message, icon));
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} o",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} Ko",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} Mo",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F2} Go"
        };
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalSeconds < 60) return $"{ts.Seconds}s";
        if (ts.TotalMinutes < 60) return $"{(int)ts.TotalMinutes}min {ts.Seconds}s";
        return $"{(int)ts.TotalHours}h {ts.Minutes}min";
    }
}
