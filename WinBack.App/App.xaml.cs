using H.NotifyIcon;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Windows;
using WinBack.App.Services;
using WinBack.App.ViewModels;
using WinBack.App.Views;
using WinBack.Core.Data;
using WinBack.Core.Services;

namespace WinBack.App;

public partial class App : Application
{
    private IHost _host = null!;
    private TaskbarIcon _trayIcon = null!;

    /// <summary>Vrai lorsque l'application est en cours d'arrêt (Shutdown appelé).</summary>
    public static bool IsShuttingDown { get; private set; }

    // ── Démarrage ─────────────────────────────────────────────────────────────

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            await InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Erreur critique au démarrage de WinBack :\n\n{ex.Message}",
                "WinBack — Erreur fatale",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async Task InitializeAsync()
    {
        _host = BuildHost();

        // Initialiser la base de données AVANT de démarrer le host
        var dbFactory = _host.Services.GetRequiredService<IDbContextFactory<WinBackContext>>();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Initialize();
        }

        // Récupérer l'icône tray depuis les ressources XAML
        _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        var notifications = _host.Services.GetRequiredService<NotificationService>();
        notifications.Initialize(_trayIcon);
        _trayIcon.TrayMouseDoubleClick += (_, _) => ShowDashboard();
        _trayIcon.TrayBalloonTipClicked += async (_, _) =>
        {
            var s = await _host.Services.GetRequiredService<ProfileService>().GetSettingsAsync();
            if (!s.ClickableNotifications) return;
            Dispatcher.Invoke(() => TrayHistory_Click(this, new RoutedEventArgs()));
        };

        // Créer la fenêtre principale et la définir comme MainWindow AVANT de démarrer le host.
        // L'UsbMonitorService (IHostedService) en a besoin pour attacher son hook WM_DEVICECHANGE.
        _dashboard = GetService<DashboardWindow>();
        MainWindow = _dashboard;

        var profileService = _host.Services.GetRequiredService<ProfileService>();
        var settings = await profileService.GetSettingsAsync();

        // Démarrer le host (lance UsbMonitorService.StartAsync qui accroche WM_DEVICECHANGE)
        await _host.StartAsync();

        if (!settings.StartMinimized)
            ShowDashboard();
        else
            _dashboard.Hide(); // Fenêtre cachée mais HWND existant pour le hook USB

        // Abonnement aux événements d'orchestration
        var orchestrator = _host.Services.GetRequiredService<BackupOrchestrator>();
        orchestrator.UnknownDriveInserted += OnUnknownDriveInserted;

        // Câbler la demande de mot de passe pour les sauvegardes chiffrées :
        // ce callback est invoqué par l'orchestrateur sur le thread appelant (non-UI),
        // donc on utilise Dispatcher.Invoke pour afficher la fenêtre sur le thread UI.
        orchestrator.RequestEncryptionKeyAsync = async profile =>
        {
            // Générer un sel PBKDF2 si le profil n'en a pas encore
            byte[]? salt = null;
            if (profile.EncryptionSalt != null)
            {
                salt = Convert.FromBase64String(profile.EncryptionSalt);
            }
            else
            {
                salt = RestoreEngine.GenerateSalt();
                profile.EncryptionSalt = Convert.ToBase64String(salt);
                await profileService.UpdateProfileAsync(profile);
            }

            byte[]? key = null;
            Dispatcher.Invoke(() =>
            {
                var promptWindow = GetService<PasswordPromptWindow>();
                promptWindow.Owner = GetOrCreateDashboard();
                promptWindow.InitForProfile(profile.Name, salt);
                if (promptWindow.ShowDialog() == true)
                    key = promptWindow.DerivedKey;
            });
            return key;
        };
        orchestrator.BackupStarted += (_, args) =>
        {
            if (args.RequiresConfirmation)
                Dispatcher.Invoke(() => AskBackupConfirmation(args.Profile, args.Drive));
        };
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        IsShuttingDown = true;
        // Nettoyer le ViewModel du dashboard pour désabonner les événements
        if (_dashboard?.DataContext is DashboardViewModel vm)
            vm.Cleanup();
        _trayIcon.Dispose();
        await _host.StopAsync(TimeSpan.FromSeconds(5));
        _host.Dispose();
        base.OnExit(e);
    }

    // ── Construction du conteneur DI ─────────────────────────────────────────

    private static IHost BuildHost() =>
        Host.CreateDefaultBuilder()
            .ConfigureServices((ctx, services) =>
            {
                // ── Base de données ─────────────────────────────────────────
                services.AddDbContextFactory<WinBackContext>(options =>
                    options.UseSqlite($"Data Source={WinBackContext.GetDatabasePath()}"));

                // ── Services Core ───────────────────────────────────────────
                services.AddSingleton<ProfileService>();
                services.AddSingleton<DiffCalculator>();
                services.AddSingleton<BackupEngine>();
                services.AddSingleton<RestoreEngine>();

                // ── Services App ────────────────────────────────────────────
                services.AddSingleton<NotificationService>();
                services.AddSingleton<BackupOrchestrator>();
                services.AddSingleton<UsbMonitorService>();
                services.AddHostedService(sp => sp.GetRequiredService<UsbMonitorService>());

                // ── ViewModels ──────────────────────────────────────────────
                services.AddTransient<DashboardViewModel>();
                services.AddTransient<ProfileEditorViewModel>();
                services.AddTransient<HistoryViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<RestoreViewModel>();

                // ── Vues ────────────────────────────────────────────────────
                services.AddTransient<DashboardWindow>();
                services.AddTransient<ProfileEditorWindow>();
                services.AddTransient<HistoryWindow>();
                services.AddTransient<SettingsWindow>();
                services.AddTransient<RestoreWindow>();
                services.AddTransient<PasswordPromptWindow>();
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                logging.AddConsole();
                // TODO : ajouter un FileLogger (ex: Serilog) pour la production
            })
            .Build();

    // ── Navigation ────────────────────────────────────────────────────────────

    public void ShowDashboard()
    {
        var win = GetOrCreateDashboard();
        win.Show();
        win.Activate();
        if (win.WindowState == WindowState.Minimized)
            win.WindowState = WindowState.Normal;
    }

    private DashboardWindow? _dashboard;
    private bool _dashboardClosedPermanently;
    private DashboardWindow GetOrCreateDashboard()
    {
        // Vérifier si la fenêtre a été fermée (Closing est annulé par Hide(),
        // mais si elle a quand même été fermée, on en recrée une).
        if (_dashboard is not null && !_dashboardClosedPermanently)
            return _dashboard;

        _dashboardClosedPermanently = false;
        _dashboard = GetService<DashboardWindow>();
        _dashboard.Closed += (_, _) => _dashboardClosedPermanently = true;
        return _dashboard;
    }

    // ── Événements tray ───────────────────────────────────────────────────────

    private void TrayOpen_Click(object sender, RoutedEventArgs e) => ShowDashboard();
    private void TrayHistory_Click(object sender, RoutedEventArgs e)
    {
        var win = GetService<HistoryWindow>();
        win.Owner = GetOrCreateDashboard();
        win.Show();
        win.Activate();
    }
    private void TraySettings_Click(object sender, RoutedEventArgs e)
    {
        var win = GetService<SettingsWindow>();
        win.Owner = GetOrCreateDashboard();
        win.ShowDialog();
    }
    private void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        Shutdown();
    }

    // ── Événements orchestrateur ──────────────────────────────────────────────

    private void OnUnknownDriveInserted(object? sender, NewDriveEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var result = MessageBox.Show(
                $"Le disque « {e.Drive.Label} » ({e.Drive.DriveLetter}:\\) n'est pas configuré.\n\n" +
                "Voulez-vous créer un profil de sauvegarde pour ce disque ?",
                "WinBack — Nouveau disque",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                ShowDashboard();
                var win = GetService<ProfileEditorWindow>();
                win.Owner = GetOrCreateDashboard();
                win.InitFromDrive(e.Drive);
                if (win.ShowDialog() == true)
                    _ = GetService<DashboardViewModel>().LoadCommand.ExecuteAsync(null);
            }
        });
    }

    private void AskBackupConfirmation(
        WinBack.Core.Models.BackupProfile profile,
        WinBack.Core.Services.DriveDetails drive)
    {
        var result = MessageBox.Show(
            $"Le disque « {profile.Name} » est connecté.\n\nDémarrer la sauvegarde maintenant ?",
            "WinBack — Sauvegarde",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
            _ = GetService<BackupOrchestrator>().StartBackupAsync(profile, drive);
    }

    // ── Helper DI ─────────────────────────────────────────────────────────────

    public static T GetService<T>() where T : notnull =>
        ((App)Current)._host.Services.GetRequiredService<T>();
}
