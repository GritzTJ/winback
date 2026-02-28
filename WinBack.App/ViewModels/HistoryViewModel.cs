using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using WinBack.Core.Models;
using WinBack.Core.Services;

namespace WinBack.App.ViewModels;

/// <summary>
/// ViewModel de la fenêtre d'historique. Charge les 100 dernières exécutions
/// et affiche le détail (fichiers traités) d'une exécution sélectionnée.
/// </summary>
public partial class HistoryViewModel : ViewModelBase
{
    private readonly ProfileService _profileService;

    /// <summary>Liste paginée des exécutions (100 dernières).</summary>
    public ObservableCollection<BackupRunDetailViewModel> Runs { get; } = [];

    /// <summary>Exécution actuellement sélectionnée dans la liste. Null si aucune.</summary>
    [ObservableProperty]
    private BackupRunDetailViewModel? _selectedRun;

    /// <summary>
    /// Entrées détaillées (fichier par fichier) de l'exécution sélectionnée.
    /// Chargées à la demande via <see cref="SelectRunCommand"/>.
    /// </summary>
    public ObservableCollection<BackupRunEntry> SelectedEntries { get; } = [];

    public HistoryViewModel(ProfileService profileService)
    {
        _profileService = profileService;
    }

    /// <summary>Charge ou recharge les 100 dernières exécutions depuis la base de données.</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        SetBusy(true);
        try
        {
            var runs = await _profileService.GetRecentRunsAsync(100);
            Runs.Clear();
            foreach (var r in runs)
                Runs.Add(new BackupRunDetailViewModel(r));
        }
        finally { SetBusy(false); }
    }

    /// <summary>
    /// Sélectionne une exécution et charge ses entrées détaillées.
    /// Passer null vide la sélection.
    /// </summary>
    [RelayCommand]
    private async Task SelectRunAsync(BackupRunDetailViewModel? run)
    {
        SelectedRun = run;
        SelectedEntries.Clear();
        if (run == null) return;

        var entries = await _profileService.GetRunEntriesAsync(run.RunId);
        foreach (var e in entries)
            SelectedEntries.Add(e);
    }
}

/// <summary>
/// Représente une exécution de sauvegarde avec ses métriques formatées pour l'affichage.
/// Données en lecture seule, calculées à la construction.
/// </summary>
public class BackupRunDetailViewModel
{
    public int RunId { get; }
    public string ProfileName { get; }
    public string DateText { get; }
    public string DurationText { get; }
    public string SummaryText { get; }
    public string SizeText { get; }
    public BackupRunStatus Status { get; }

    /// <summary>Vrai si l'exécution était une simulation (dry run) sans écriture réelle.</summary>
    public bool IsDryRun { get; }

    /// <summary>Icône Unicode représentant le statut de l'exécution.</summary>
    public string StatusIcon => Status switch
    {
        BackupRunStatus.Success        => "✓",
        BackupRunStatus.PartialSuccess => "⚠",
        BackupRunStatus.Error          => "✗",
        BackupRunStatus.Cancelled      => "—",
        BackupRunStatus.Running        => "…",
        _                              => "?"
    };

    public BackupRunDetailViewModel(BackupRun run)
    {
        RunId       = run.Id;
        ProfileName = run.Profile?.Name ?? "Profil inconnu";
        DateText    = run.StartedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
        IsDryRun    = run.IsDryRun;
        Status      = run.Status;

        DurationText = run.Duration.HasValue
            ? FormatDuration(run.Duration.Value)
            : run.Status == BackupRunStatus.Running ? "En cours…" : "—";

        // Résumé condensé : +ajoutés ~modifiés -supprimés (⚠erreurs si présentes)
        SummaryText = run.TotalFiles == 0 && run.Status == BackupRunStatus.Success
            ? "Aucun changement"
            : $"+{run.FilesAdded}  ~{run.FilesModified}  -{run.FilesDeleted}" +
              (run.FilesErrored > 0 ? $"  ⚠{run.FilesErrored} erreur(s)" : "");

        SizeText = FormatBytes(run.BytesTransferred);
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalSeconds < 60) return $"{ts.Seconds}s";
        if (ts.TotalMinutes < 60) return $"{(int)ts.TotalMinutes}min {ts.Seconds}s";
        return $"{(int)ts.TotalHours}h {ts.Minutes}min";
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        0                        => "0 o",
        < 1024                   => $"{bytes} o",
        < 1024 * 1024            => $"{bytes / 1024.0:F1} Ko",
        < 1024L * 1024 * 1024    => $"{bytes / (1024.0 * 1024):F1} Mo",
        _                        => $"{bytes / (1024.0 * 1024 * 1024):F2} Go"
    };
}
