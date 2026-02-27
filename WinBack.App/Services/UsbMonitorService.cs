using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WinBack.Core.Services;

namespace WinBack.App.Services;

/// <summary>
/// Service de surveillance d'insertion/retrait de disques via WM_DEVICECHANGE.
/// Doit être démarré depuis le thread UI après que la fenêtre principale soit créée.
/// </summary>
public class UsbMonitorService : IHostedService
{
    // Messages Windows
    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const int DBT_DEVTYP_VOLUME = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct DEV_BROADCAST_HDR
    {
        public uint dbch_size;
        public uint dbch_devicetype;
        public uint dbch_reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEV_BROADCAST_VOLUME
    {
        public uint dbcv_size;
        public uint dbcv_devicetype;
        public uint dbcv_reserved;
        public uint dbcv_unitmask;  // Bitmask des lettres de lecteur (bit 0=A, bit 1=B, …)
        public ushort dbcv_flags;
    }

    private readonly BackupOrchestrator _orchestrator;
    private readonly ILogger<UsbMonitorService> _logger;
    private HwndSource? _hwndSource;

    public event EventHandler<DriveEventArgs>? DriveArrived;
    public event EventHandler<DriveEventArgs>? DriveRemoved;

    public UsbMonitorService(BackupOrchestrator orchestrator, ILogger<UsbMonitorService> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Hook sur la fenêtre principale WPF depuis le thread UI
        Application.Current.Dispatcher.Invoke(AttachToWindow);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _hwndSource?.RemoveHook(WndProc);
            _hwndSource = null;
        });
        return Task.CompletedTask;
    }

    private void AttachToWindow()
    {
        var window = Application.Current.MainWindow;
        if (window == null)
        {
            _logger.LogError("Impossible d'attacher WM_DEVICECHANGE : MainWindow est null");
            return;
        }

        var helper = new WindowInteropHelper(window);
        helper.EnsureHandle(); // Crée le HWND même si la fenêtre n'est pas visible
        _hwndSource = HwndSource.FromHwnd(helper.Handle);
        _hwndSource?.AddHook(WndProc);
        _logger.LogInformation("Surveillance USB démarrée (WM_DEVICECHANGE) sur HWND={Handle}", helper.Handle);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_DEVICECHANGE) return IntPtr.Zero;

        int eventType = wParam.ToInt32();
        if (eventType is not (DBT_DEVICEARRIVAL or DBT_DEVICEREMOVECOMPLETE))
            return IntPtr.Zero;

        if (lParam == IntPtr.Zero) return IntPtr.Zero;

        var header = Marshal.PtrToStructure<DEV_BROADCAST_HDR>(lParam);
        if (header.dbch_devicetype != DBT_DEVTYP_VOLUME)
            return IntPtr.Zero;

        var vol = Marshal.PtrToStructure<DEV_BROADCAST_VOLUME>(lParam);
        var driveLetters = GetDriveLettersFromMask(vol.dbcv_unitmask);

        foreach (var letter in driveLetters)
        {
            var drivePath = $"{letter}:\\";

            if (eventType == DBT_DEVICEARRIVAL)
            {
                _logger.LogInformation("Disque inséré : {Drive}", drivePath);
                OnDriveArrived(letter);
            }
            else
            {
                _logger.LogInformation("Disque retiré : {Drive}", drivePath);
                DriveRemoved?.Invoke(this, new DriveEventArgs(letter, drivePath, null));
            }
        }

        return IntPtr.Zero;
    }

    private void OnDriveArrived(char driveLetter)
    {
        var drivePath = $"{driveLetter}:\\";
        Task.Run(async () =>
        {
            // Attendre que le disque soit entièrement monté
            await Task.Delay(500);

            var details = DriveIdentifier.GetDriveDetails(drivePath);
            if (details == null)
            {
                _logger.LogWarning("Impossible d'identifier le disque {Drive}", drivePath);
                return;
            }

            _logger.LogInformation("Disque identifié : {Label} ({Guid})", details.Label, details.VolumeGuid);

            // Notifier les abonnés UI
            await Application.Current.Dispatcher.InvokeAsync(() =>
                DriveArrived?.Invoke(this, new DriveEventArgs(driveLetter, drivePath, details)));

            // Déléguer au BackupOrchestrator
            await _orchestrator.OnDriveInsertedAsync(details);
        });
    }

    private static IEnumerable<char> GetDriveLettersFromMask(uint unitMask)
    {
        for (int i = 0; i < 26; i++)
        {
            if ((unitMask & (1u << i)) != 0)
                yield return (char)('A' + i);
        }
    }
}

public class DriveEventArgs : EventArgs
{
    public char DriveLetter { get; }
    public string DrivePath { get; }
    public DriveDetails? Details { get; }

    public DriveEventArgs(char driveLetter, string drivePath, DriveDetails? details)
    {
        DriveLetter = driveLetter;
        DrivePath = drivePath;
        Details = details;
    }
}
