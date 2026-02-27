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

    // ── Démarrage ─────────────────────────────────────────────────────────────

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

        // Créer la fenêtre principale et la définir comme MainWindow AVANT de démarrer le host.
        // L'UsbMonitorService (IHostedService) en a besoin pour attacher son hook WM_DEVICECHANGE.
        _dashboard = GetService<DashboardWindow>();
        MainWindow = _dashboard;

        var settings = await _host.Services.GetRequiredService<ProfileService>().GetSettingsAsync();

        // Démarrer le host (lance UsbMonitorService.StartAsync qui accroche WM_DEVICECHANGE)
        await _host.StartAsync();

        if (!settings.StartMinimized)
            ShowDashboard();
        else
            _dashboard.Hide(); // Fenêtre cachée mais HWND existant pour le hook USB

        // Abonnement aux événements d'orchestration
        var orchestrator = _host.Services.GetRequiredService<BackupOrchestrator>();
        orchestrator.UnknownDriveInserted += OnUnknownDriveInserted;
        orchestrator.BackupStarted += (_, args) =>
        {
            if (args.RequiresConfirmation)
                Dispatcher.Invoke(() => AskBackupConfirmation(args.Profile, args.Drive));
        };
    }

    protected override async void OnExit(ExitEventArgs e)
    {
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

                // ── Vues ────────────────────────────────────────────────────
                services.AddTransient<DashboardWindow>();
                services.AddTransient<ProfileEditorWindow>();
                services.AddTransient<HistoryWindow>();
                services.AddTransient<SettingsWindow>();
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
    private DashboardWindow GetOrCreateDashboard()
    {
        if (_dashboard is null || !_dashboard.IsLoaded)
            _dashboard = GetService<DashboardWindow>();
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
