using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.IO;
using WinBack.Core.Models;
using WinBack.Core.Services;

namespace WinBack.App.ViewModels;

/// <summary>
/// ViewModel de la fenêtre Paramètres. Gère les préférences utilisateur
/// persistées en base (via <see cref="AppSettings"/>) et le démarrage automatique
/// avec Windows (clé de registre <c>HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run</c>).
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly ProfileService _profileService;

    // Clé de registre pour le démarrage automatique (HKCU, pas besoin de droits admin)
    private const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "WinBack";

    /// <summary>Lance WinBack automatiquement au démarrage de Windows.</summary>
    [ObservableProperty]
    private bool _startWithWindows;

    /// <summary>Affiche des notifications ballon lors des sauvegardes.</summary>
    [ObservableProperty]
    private bool _showNotifications = true;

    /// <summary>
    /// Active le mode avancé : affiche le GUID du volume dans les profils
    /// et expose des options supplémentaires normalement masquées.
    /// </summary>
    [ObservableProperty]
    private bool _advancedMode;

    /// <summary>Démarre l'application réduite dans la barre système (sans ouvrir la fenêtre).</summary>
    [ObservableProperty]
    private bool _startMinimized = true;

    /// <summary>Niveau de verbosité des logs (1=erreurs, 3=info, 5=debug).</summary>
    [ObservableProperty]
    private int _logLevel = 3;

    /// <summary>Dossier de destination des fichiers de log. Null = dossier par défaut.</summary>
    [ObservableProperty]
    private string _logDirectory = string.Empty;

    /// <summary>Version de l'application lue depuis l'assembly (ex. "0.1.0").</summary>
    [ObservableProperty]
    private string _appVersion = string.Empty;

    /// <summary>Chemin complet du fichier de base de données SQLite.</summary>
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

    /// <summary>
    /// Charge les paramètres depuis la base de données et synchronise
    /// l'état du démarrage automatique avec la clé de registre réelle
    /// (priorité au registre en cas de désynchronisation).
    /// </summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        var s = await _profileService.GetSettingsAsync();
        StartWithWindows  = s.StartWithWindows;
        ShowNotifications = s.ShowNotifications;
        AdvancedMode      = s.AdvancedMode;
        StartMinimized    = s.StartMinimized;
        LogLevel          = s.LogLevel;
        LogDirectory      = s.LogDirectory ?? GetDefaultLogDirectory();

        // La source de vérité pour le démarrage automatique est le registre,
        // pas la base de données (l'utilisateur peut avoir modifié le registre manuellement)
        StartWithWindows = IsStartupEnabled();
    }

    /// <summary>
    /// Persiste les paramètres en base et applique immédiatement
    /// le changement de démarrage automatique dans le registre.
    /// </summary>
    [RelayCommand]
    public async Task SaveAsync()
    {
        SetBusy(true, "Enregistrement…");
        try
        {
            var s = new AppSettings
            {
                StartWithWindows  = StartWithWindows,
                ShowNotifications = ShowNotifications,
                AdvancedMode      = AdvancedMode,
                StartMinimized    = StartMinimized,
                LogLevel          = LogLevel,
                // Ne persiste pas le chemin s'il correspond au chemin par défaut
                LogDirectory = LogDirectory == GetDefaultLogDirectory() ? null : LogDirectory
            };
            await _profileService.SaveSettingsAsync(s);
            ApplyStartupSetting(StartWithWindows);
            StatusMessage = "Paramètres enregistrés.";
        }
        finally { SetBusy(false); }
    }

    /// <summary>Ouvre un sélecteur de dossier pour choisir le répertoire de logs.</summary>
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

    /// <summary>Ouvre le dossier de logs dans l'Explorateur Windows.</summary>
    [RelayCommand]
    private void OpenLogDirectory()
    {
        var dir = string.IsNullOrWhiteSpace(LogDirectory)
            ? GetDefaultLogDirectory()
            : LogDirectory;
        if (Directory.Exists(dir))
            System.Diagnostics.Process.Start("explorer.exe", dir);
    }

    /// <summary>Ouvre le dossier contenant la base de données SQLite dans l'Explorateur.</summary>
    [RelayCommand]
    private void OpenDatabaseFolder()
    {
        var dir = Path.GetDirectoryName(DatabasePath);
        if (dir != null && Directory.Exists(dir))
            System.Diagnostics.Process.Start("explorer.exe", dir);
    }

    // ── Registre démarrage automatique ──────────────────────────────────────────

    /// <summary>
    /// Ajoute ou supprime l'entrée Run dans le registre HKCU.
    /// Utilise le chemin de l'exécutable en cours pour la valeur de la clé.
    /// </summary>
    private static void ApplyStartupSetting(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, writable: true);
        if (key == null) return;

        if (enable)
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                          ?? string.Empty;
            key.SetValue(AppName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }

    /// <summary>Vérifie si la clé de démarrage automatique est présente dans le registre.</summary>
    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, writable: false);
        return key?.GetValue(AppName) != null;
    }

    /// <summary>Retourne le dossier de logs par défaut : %LOCALAPPDATA%\WinBack\Logs\</summary>
    private static string GetDefaultLogDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinBack", "Logs");
}
