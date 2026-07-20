using H.NotifyIcon;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WinBack.Core.Models;

namespace WinBack.App.Services;

/// <summary>
/// Gère les notifications utilisateur via l'icône de la barre système.
/// Utilise Shell_NotifyIcon (NIIF_INFO/WARNING/ERROR) en P/Invoke direct,
/// compatible avec tous les packages H.NotifyIcon.
/// </summary>
public class NotificationService
{
    // ── Win32 API ──────────────────────────────────────────────────────────────

    private const uint NIM_MODIFY       = 0x01;
    private const uint NIF_INFO         = 0x10;
    private const uint NIIF_INFO        = 0x01;
    private const uint NIIF_WARNING     = 0x02;
    private const uint NIIF_ERROR       = 0x03;
    private const uint NIIF_NOSOUND     = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint    cbSize;
        public IntPtr  hWnd;
        public uint    uID;
        public uint    uFlags;
        public uint    uCallbackMessage;
        public IntPtr  hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string  szTip;
        public uint    dwState;
        public uint    dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string  szInfo;
        public uint    uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string  szInfoTitle;
        public uint    dwInfoFlags;
        public Guid    guidItem;
        public IntPtr  hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA pnid);

    // ── État ───────────────────────────────────────────────────────────────────

    private TaskbarIcon? _trayIcon;

    public void Initialize(TaskbarIcon trayIcon)
    {
        _trayIcon = trayIcon;
    }

    // ── API publique ───────────────────────────────────────────────────────────

    public void NotifyDriveDetected(string profileName)
    {
        ShowBalloon("WinBack — Disque détecté",
            $"Sauvegarde de « {profileName} » en cours…", NIIF_INFO);
    }

    public void NotifyBackupComplete(BackupRun run, string profileName)
    {
        if (run.Status == BackupRunStatus.Success || run.Status == BackupRunStatus.PartialSuccess)
        {
            var duration = run.Duration.HasValue ? FormatDuration(run.Duration.Value) : "";

            var msg = run.TotalFiles == 0
                ? "Aucun changement détecté."
                : $"+{run.FilesAdded} ajouté(s)  ~{run.FilesModified} modifié(s)  -{run.FilesDeleted} supprimé(s)\n"
                + $"{FormatBytes(run.BytesTransferred)}{(duration.Length > 0 ? $" en {duration}" : "")}";

            ShowBalloon($"WinBack — {profileName}", msg,
                run.FilesErrored > 0 ? NIIF_WARNING : NIIF_INFO);
        }
        else if (run.Status == BackupRunStatus.Error)
        {
            ShowBalloon($"WinBack — Erreur ({profileName})",
                run.ErrorMessage ?? "Une erreur est survenue.", NIIF_ERROR);
        }
    }

    public void NotifyNewDrive(string driveLabel, string drivePath)
    {
        ShowBalloon("WinBack — Nouveau disque",
            $"Disque « {driveLabel} » ({drivePath}) détecté.\nCliquez pour configurer une sauvegarde.",
            NIIF_INFO);
    }

    public void NotifyError(string message)
    {
        ShowBalloon("WinBack — Erreur", message, NIIF_ERROR);
    }

    // ── Implémentation P/Invoke ────────────────────────────────────────────────

    private void ShowBalloon(string title, string message, uint niifFlags)
    {
        var trayIcon = _trayIcon;
        if (trayIcon == null) return;

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            try
            {
                // Récupérer le handle de fenêtre via le MessageWindow interne de H.NotifyIcon
                var field = typeof(TaskbarIcon).GetField("_messageSink",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var sink = field?.GetValue(trayIcon);
                var hWndProp = sink?.GetType().GetProperty("Handle",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var hWnd = (IntPtr?)hWndProp?.GetValue(sink) ?? IntPtr.Zero;

                if (hWnd == IntPtr.Zero)
                {
                    Trace.WriteLine("[Notifications] HWND de l'icône introuvable — notification ignorée. " +
                                    "L'API interne de H.NotifyIcon a probablement changé.");
                    return;
                }

                var nid = new NOTIFYICONDATA
                {
                    cbSize        = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd          = hWnd,
                    uID           = ResolveIconId(trayIcon),
                    uFlags        = NIF_INFO,
                    szInfo        = message.Length > 255 ? message[..255] : message,
                    szInfoTitle   = title.Length > 63 ? title[..63] : title,
                    dwInfoFlags   = niifFlags | NIIF_NOSOUND,
                    uTimeoutOrVersion = 10000,
                    szTip         = string.Empty
                };

                // NIM_MODIFY ne fait rien si (hWnd, uID) ne désigne pas une icône existante :
                // sans ce log, une notification qui n'apparaît jamais serait indétectable.
                if (!Shell_NotifyIcon(NIM_MODIFY, ref nid))
                    Trace.WriteLine($"[Notifications] Shell_NotifyIcon a échoué (uID={nid.uID}, " +
                                    $"win32={Marshal.GetLastWin32Error()}).");
            }
            catch (Exception ex)
            {
                // Une notification ratée ne doit pas interrompre la sauvegarde,
                // mais elle doit rester diagnosticable.
                Trace.WriteLine($"[Notifications] Échec de l'affichage : {ex}");
            }
        });
    }

    /// <summary>
    /// Identifiant de l'icône dans la barre système.
    /// H.NotifyIcon attribue cet ID lui-même ; le coder en dur ferait échouer
    /// silencieusement NIM_MODIFY dès que la bibliothèque en choisit un autre.
    /// </summary>
    private static uint ResolveIconId(TaskbarIcon trayIcon)
    {
        try
        {
            var prop = typeof(TaskbarIcon).GetProperty("Id",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var value = prop?.GetValue(trayIcon);
            if (value != null)
                return Convert.ToUInt32(value);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Notifications] Impossible de lire l'ID de l'icône : {ex.Message}");
        }
        return 1; // Repli sur le comportement historique
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024             => $"{bytes} o",
        < 1024 * 1024      => $"{bytes / 1024.0:F1} Ko",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} Mo",
        _                  => $"{bytes / (1024.0 * 1024 * 1024):F2} Go"
    };

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalSeconds < 60) return $"{ts.Seconds}s";
        if (ts.TotalMinutes < 60) return $"{(int)ts.TotalMinutes}min {ts.Seconds}s";
        return $"{(int)ts.TotalHours}h {ts.Minutes}min";
    }
}
