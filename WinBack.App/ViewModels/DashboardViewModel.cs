using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using WinBack.App.Services;
using WinBack.Core.Models;
using WinBack.Core.Services;

namespace WinBack.App.ViewModels;

/// <summary>
/// ViewModel de la fenêtre principale. Affiche la liste des profils de sauvegarde
/// et les dernières exécutions. S'abonne aux événements de l'orchestrateur pour
/// mettre à jour l'état des cartes en temps réel (progression, statut).
/// </summary>
public partial class DashboardViewModel : ViewModelBase
{
    private readonly ProfileService _profileService;
    private readonly BackupOrchestrator _orchestrator;

    /// <summary>Liste des profils affichés sous forme de cartes.</summary>
    public ObservableCollection<ProfileCardViewModel> Profiles { get; } = [];

    /// <summary>Les 10 dernières exécutions toutes sauvegardes confondues.</summary>
    public ObservableCollection<RecentRunViewModel> RecentRuns { get; } = [];

    /// <summary>
    /// Vrai si au moins un profil existe. Utilisé pour afficher l'état vide
    /// ("Aucun profil configuré") ou la liste des cartes.
    /// </summary>
    [ObservableProperty]
    private bool _hasProfiles;

    /// <summary>Progression globale (0.0–1.0) agrégée de tous les profils en cours.</summary>
    [ObservableProperty]
    private double _overallProgress;

    /// <summary>État de la barre de progression dans la barre des tâches.</summary>
    [ObservableProperty]
    private System.Windows.Shell.TaskbarItemProgressState _taskbarProgressState
        = System.Windows.Shell.TaskbarItemProgressState.None;

    public DashboardViewModel(ProfileService profileService, BackupOrchestrator orchestrator)
    {
        _profileService = profileService;
        _orchestrator = orchestrator;

        // Mise à jour de la progression en temps réel lors d'une sauvegarde
        _orchestrator.BackupStarted += OnBackupStarted;
        _orchestrator.BackupCompleted += OnBackupCompleted;
    }

    /// <summary>Charge les profils et l'historique récent depuis la base de données.</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        SetBusy(true, "Chargement…");
        try
        {
            var profiles = await _profileService.GetAllProfilesAsync();
            var runs = await _profileService.GetRecentRunsAsync(10);

            Profiles.Clear();
            foreach (var p in profiles)
                Profiles.Add(new ProfileCardViewModel(p, _orchestrator, p.EnableHashVerification));

            RecentRuns.Clear();
            foreach (var r in runs)
                RecentRuns.Add(new RecentRunViewModel(r));

            HasProfiles = Profiles.Count > 0;
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Supprime un profil et recharge la liste.</summary>
    [RelayCommand]
    public async Task DeleteProfileAsync(int profileId)
    {
        await _profileService.DeleteProfileAsync(profileId);
        await LoadAsync();
    }

    // ── Export / Import ───────────────────────────────────────────────────────

    /// <summary>
    /// Sérialise le profil en JSON WinBack et retourne la chaîne.
    /// Appelé par le code-behind de <see cref="DashboardWindow"/> qui gère
    /// la boîte de dialogue de sauvegarde de fichier.
    /// </summary>
    public Task<string> ExportProfileAsync(int profileId)
        => _profileService.ExportProfileAsync(profileId);

    /// <summary>
    /// Importe un profil depuis un JSON WinBack, puis recharge la liste des profils.
    /// Appelé par le code-behind de <see cref="DashboardWindow"/> après ouverture du fichier.
    /// </summary>
    public async Task ImportProfileAsync(string json)
    {
        await _profileService.ImportProfileAsync(json);
        await LoadAsync();
    }

    private void RecalcOverallProgress()
    {
        var running = Profiles.Where(p => p.IsRunning).ToList();
        if (running.Count == 0)
        {
            TaskbarProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
            OverallProgress = 0;
            return;
        }
        OverallProgress = running.Average(p => p.ProgressPercent) / 100.0;
        TaskbarProgressState = running.All(p => p.IsPaused)
            ? System.Windows.Shell.TaskbarItemProgressState.Paused
            : System.Windows.Shell.TaskbarItemProgressState.Normal;
    }

    /// <summary>
    /// Appelé par l'orchestrateur quand une sauvegarde démarre.
    /// Met à jour la carte correspondante avec l'indicateur de progression.
    /// </summary>
    private void OnBackupStarted(object? sender, BackupStartedEventArgs e)
    {
        var card = Profiles.FirstOrDefault(p => p.ProfileId == e.Profile.Id);
        if (card == null) return;

        // Mise à jour UI obligatoirement sur le thread principal
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            card.IsRunning = true;
            card.IsPaused  = false; // réinitialiser l'état pause au démarrage d'une nouvelle sauvegarde
            if (e.Progress != null)
            {
                card.ProgressText = e.Progress.CurrentFile;
                card.ProgressPercent = e.Progress.TotalFiles > 0
                    ? (int)(e.Progress.FilesProcessed * 100.0 / e.Progress.TotalFiles)
                    : 0;
            }
            RecalcOverallProgress();
        });
    }

    /// <summary>
    /// Appelé par l'orchestrateur quand une sauvegarde se termine.
    /// Met à jour le statut de la carte et rafraîchit l'historique récent.
    /// Le gestionnaire est <c>async void</c> (imposé par la signature de l'événement) :
    /// toute exception y est attrapée localement pour éviter un crash silencieux du processus.
    /// </summary>
    private async void OnBackupCompleted(object? sender, BackupCompletedEventArgs e)
    {
        try
        {
            var card = Profiles.FirstOrDefault(p => p.ProfileId == e.Profile.Id);
            if (card != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    card.IsRunning     = false;
                    card.IsPaused      = false; // garantir la réinitialisation si annulée en pause
                    card.LastRunStatus = e.Run.Status;
                    card.LastRunDate   = e.Run.FinishedAt;
                    RecalcOverallProgress();
                });
            }

            // Rafraîchir la liste des exécutions récentes sur le thread UI
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                var runs = await _profileService.GetRecentRunsAsync(10);
                RecentRuns.Clear();
                foreach (var r in runs)
                    RecentRuns.Add(new RecentRunViewModel(r));
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DashboardViewModel] OnBackupCompleted : {ex}");
        }
    }
}

// ── ViewModels enfants ─────────────────────────────────────────────────────────

/// <summary>
/// Représente un profil de sauvegarde sous forme de carte dans le dashboard.
/// Expose les propriétés d'état mises à jour en temps réel (progression, statut).
/// </summary>
public partial class ProfileCardViewModel : ObservableObject
{
    private readonly BackupOrchestrator _orchestrator;
    private readonly bool _hasHashes;

    public int ProfileId { get; }
    public string Name { get; }
    public string VolumeGuid { get; }

    /// <summary>Nombre de paires source→destination actives dans ce profil.</summary>
    public int PairCount { get; }

    /// <summary>Vrai si la vérification d'intégrité est activée (des hashs sont disponibles).</summary>
    public bool HasHashes { get; }

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private BackupRunStatus? _lastRunStatus;
    [ObservableProperty] private DateTime? _lastRunDate;

    // ── Pause ─────────────────────────────────────────────────────────────────

    /// <summary>Vrai si la sauvegarde est actuellement en pause.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseResumeLabel))]
    private bool _isPaused;

    /// <summary>Texte du bouton Pause/Reprendre selon l'état courant.</summary>
    public string PauseResumeLabel => IsPaused ? "▶ Reprendre" : "⏸ Pause";

    // ── Prévisualisation ──────────────────────────────────────────────────────

    /// <summary>Vrai pendant le calcul du diff de prévisualisation.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    private bool _isPreviewing;

    public ProfileCardViewModel(BackupProfile profile, BackupOrchestrator orchestrator, bool hasHashes = false)
    {
        _orchestrator = orchestrator;
        _hasHashes = hasHashes;
        ProfileId = profile.Id;
        Name = profile.Name;
        VolumeGuid = profile.VolumeGuid;
        PairCount = profile.Pairs.Count(p => p.IsActive);
        HasHashes = hasHashes;
    }

    /// <summary>Date de la dernière exécution en langage naturel (ex. "il y a 3h").</summary>
    public string LastRunText => LastRunDate.HasValue
        ? FormatRelativeDate(LastRunDate.Value)
        : "Jamais sauvegardé";

    /// <summary>Icône Unicode représentant le statut de la dernière exécution.</summary>
    public string StatusIcon => LastRunStatus switch
    {
        BackupRunStatus.Success        => "✓",
        BackupRunStatus.PartialSuccess => "⚠",
        BackupRunStatus.Error          => "✗",
        BackupRunStatus.Cancelled      => "—",
        _                              => "?"
    };

    /// <summary>Annule la sauvegarde en cours pour ce profil.</summary>
    [RelayCommand]
    private async Task CancelAsync() => await _orchestrator.CancelBackupAsync(ProfileId);

    /// <summary>Bascule entre pause et reprise de la sauvegarde en cours.</summary>
    [RelayCommand]
    private async Task PauseResumeAsync()
    {
        if (IsPaused)
            await _orchestrator.ResumeBackupAsync(ProfileId);
        else
            await _orchestrator.PauseBackupAsync(ProfileId);
        IsPaused = !IsPaused;
    }

    /// <summary>Calcule le diff de la prochaine sauvegarde sans rien écrire.</summary>
    [RelayCommand(CanExecute = nameof(CanPreview))]
    private async Task PreviewAsync()
    {
        IsPreviewing = true;
        try
        {
            var result = await _orchestrator.PreviewBackupAsync(ProfileId);
            var msg = result.HasChanges
                ? $"Aperçu de la prochaine sauvegarde\n\n" +
                  $"+  Ajouts    : {result.FilesAdded}\n" +
                  $"~  Modifiés  : {result.FilesModified}\n" +
                  $"-  Supprimés : {result.FilesDeleted}\n\n" +
                  $"{result.PairsChecked} dossier(s) analysé(s)"
                : $"Aucun changement détecté.\n\n{result.PairsChecked} dossier(s) analysé(s)";
            System.Windows.MessageBox.Show(msg, "WinBack — Aperçu de sauvegarde",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Impossible de calculer l'aperçu :\n{ex.Message}",
                "WinBack — Erreur", System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
        finally { IsPreviewing = false; }
    }

    private bool CanPreview => !IsPreviewing;

    /// <summary>Lance un audit d'intégrité à la demande pour ce profil.</summary>
    [RelayCommand]
    private async Task RunAuditAsync()
    {
        if (!_hasHashes)
        {
            System.Windows.MessageBox.Show(
                "Activez la vérification d'intégrité pour ce profil afin de pouvoir lancer l'audit.",
                "WinBack — Audit", System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = $"Sélectionner la racine de la sauvegarde pour « {Name} »"
        };
        if (dialog.ShowDialog() != true) return;

        IsRunning = true;
        try
        {
            var result = await _orchestrator.RunAuditAsync(ProfileId, dialog.FolderName);

            var msg = $"Audit terminé — {result.Total} fichier(s) vérifiés\n\n" +
                      $"✓ OK       : {result.Ok}\n" +
                      $"✗ Manquant : {result.Missing}\n" +
                      $"⚠ Corrompu : {result.Corrupted}";
            if (result.CorruptedPaths.Count > 0)
                msg += "\n\nFichiers corrompus :\n" + string.Join("\n", result.CorruptedPaths.Take(10));

            System.Windows.MessageBox.Show(msg, "WinBack — Audit d'intégrité",
                System.Windows.MessageBoxButton.OK,
                result.Corrupted > 0 || result.Missing > 0
                    ? System.Windows.MessageBoxImage.Warning
                    : System.Windows.MessageBoxImage.Information);
        }
        finally { IsRunning = false; }
    }

    /// <summary>
    /// Formate une date en texte relatif lisible.
    /// Ex. : "À l'instant", "il y a 5 min", "il y a 2j", "14/03/2025".
    /// </summary>
    private static string FormatRelativeDate(DateTime date)
    {
        var diff = DateTime.UtcNow - date.ToUniversalTime();
        if (diff.TotalMinutes < 1) return "À l'instant";
        if (diff.TotalHours < 1)   return $"il y a {(int)diff.TotalMinutes} min";
        if (diff.TotalDays < 1)    return $"il y a {(int)diff.TotalHours}h";
        if (diff.TotalDays < 7)    return $"il y a {(int)diff.TotalDays}j";
        return date.ToLocalTime().ToString("dd/MM/yyyy");
    }
}

/// <summary>
/// Représente une ligne dans la liste des exécutions récentes du dashboard.
/// Données en lecture seule, calculées à la construction.
/// </summary>
public class RecentRunViewModel
{
    public string ProfileName { get; }
    public string DateText { get; }
    public string SummaryText { get; }
    public BackupRunStatus Status { get; }

    /// <summary>Vrai si l'exécution s'est terminée avec succès (total ou partiel).</summary>
    public bool IsSuccess => Status is BackupRunStatus.Success or BackupRunStatus.PartialSuccess;

    public RecentRunViewModel(BackupRun run)
    {
        ProfileName = run.Profile?.Name ?? "?";
        DateText    = run.StartedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        Status      = run.Status;
        SummaryText = run.TotalFiles == 0
            ? "Aucun changement"
            : $"+{run.FilesAdded} ~{run.FilesModified} -{run.FilesDeleted}";
    }
}
