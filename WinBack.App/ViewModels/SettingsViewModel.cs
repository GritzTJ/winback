using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.IO;
using WinBack.Core.Models;
using WinBack.Core.Services;

namespace WinBack.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ProfileService _profileService;
    private const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "WinBack";

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _showNotifications = true;

    [ObservableProperty]
    private bool _advancedMode;

    [ObservableProperty]
    private bool _startMinimized = true;

    [ObservableProperty]
    private int _logLevel = 3;

    [ObservableProperty]
    private string _logDirectory = string.Empty;

    [ObservableProperty]
    private string _appVersion = string.Empty;

    [ObservableProperty]
    private string _databasePath = string.Empty;

    public SettingsViewModel(ProfileService profileService)
    {
        _profileService = profileService;
        AppVersion = System.Reflection.Assembly
            .GetExecutingAssembly()
            .GetName().Version?.ToString(3) ?? "1.0.0";
        DatabasePath = WinBack.Core.Data.WinBackContext.GetDatabasePath();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        var s = await _profileService.GetSettingsAsync();
        StartWithWindows = s.StartWithWindows;
        ShowNotifications = s.ShowNotifications;
        AdvancedMode = s.AdvancedMode;
        StartMinimized = s.StartMinimized;
        LogLevel = s.LogLevel;
        LogDirectory = s.LogDirectory ?? GetDefaultLogDirectory();

        // Vérifier la valeur réelle dans le registre
        StartWithWindows = IsStartupEnabled();
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        SetBusy(true, "Enregistrement…");
        try
        {
            var s = new AppSettings
            {
                StartWithWindows = StartWithWindows,
                ShowNotifications = ShowNotifications,
                AdvancedMode = AdvancedMode,
                StartMinimized = StartMinimized,
                LogLevel = LogLevel,
                LogDirectory = LogDirectory == GetDefaultLogDirectory() ? null : LogDirectory
            };
            await _profileService.SaveSettingsAsync(s);
            ApplyStartupSetting(StartWithWindows);
            StatusMessage = "Paramètres enregistrés.";
        }
        finally { SetBusy(false); }
    }

    [RelayCommand]
    private void BrowseLogDirectory()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choisir le dossier de logs",
            InitialDirectory = LogDirectory
        };
        if (dialog.ShowDialog() == true)
            LogDirectory = dialog.FolderName;
    }

    [RelayCommand]
    private void OpenLogDirectory()
    {
        var dir = string.IsNullOrWhiteSpace(LogDirectory)
            ? GetDefaultLogDirectory()
            : LogDirectory;
        if (Directory.Exists(dir))
            System.Diagnostics.Process.Start("explorer.exe", dir);
    }

    [RelayCommand]
    private void OpenDatabaseFolder()
    {
        var dir = Path.GetDirectoryName(DatabasePath);
        if (dir != null && Directory.Exists(dir))
            System.Diagnostics.Process.Start("explorer.exe", dir);
    }

    // ── Registre démarrage automatique ──────────────────────────────────────

    private static void ApplyStartupSetting(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, writable: true);
        if (key == null) return;

        if (enable)
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            key.SetValue(AppName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, writable: false);
        return key?.GetValue(AppName) != null;
    }

    private static string GetDefaultLogDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinBack", "Logs");
}
