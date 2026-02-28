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
                Profiles.Add(new ProfileCardViewModel(p, _orchestrator));

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
            if (e.Progress != null)
            {
                card.ProgressText = e.Progress.CurrentFile;
                card.ProgressPercent = e.Progress.TotalFiles > 0
                    ? (int)(e.Progress.FilesProcessed * 100.0 / e.Progress.TotalFiles)
                    : 0;
            }
        });
    }

    /// <summary>
    /// Appelé par l'orchestrateur quand une sauvegarde se termine.
    /// Met à jour le statut de la carte et rafraîchit l'historique récent.
    /// </summary>
    private async void OnBackupCompleted(object? sender, BackupCompletedEventArgs e)
    {
        var card = Profiles.FirstOrDefault(p => p.ProfileId == e.Profile.Id);
        if (card != null)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                card.IsRunning = false;
                card.LastRunStatus = e.Run.Status;
                card.LastRunDate = e.Run.FinishedAt;
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
}

// ── ViewModels enfants ─────────────────────────────────────────────────────────

/// <summary>
/// Représente un profil de sauvegarde sous forme de carte dans le dashboard.
/// Expose les propriétés d'état mises à jour en temps réel (progression, statut).
/// </summary>
public partial class ProfileCardViewModel : ObservableObject
{
    private readonly BackupOrchestrator _orchestrator;

    public int ProfileId { get; }
    public string Name { get; }
    public string VolumeGuid { get; }

    /// <summary>Nombre de paires source→destination actives dans ce profil.</summary>
    public int PairCount { get; }

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private BackupRunStatus? _lastRunStatus;
    [ObservableProperty] private DateTime? _lastRunDate;

    public ProfileCardViewModel(BackupProfile profile, BackupOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
        ProfileId = profile.Id;
        Name = profile.Name;
        VolumeGuid = profile.VolumeGuid;
        PairCount = profile.Pairs.Count(p => p.IsActive);
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
