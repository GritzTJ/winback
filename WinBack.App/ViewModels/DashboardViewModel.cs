using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using WinBack.App.Services;
using WinBack.Core.Models;
using WinBack.Core.Services;

namespace WinBack.App.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly ProfileService _profileService;
    private readonly BackupOrchestrator _orchestrator;

    public ObservableCollection<ProfileCardViewModel> Profiles { get; } = [];
    public ObservableCollection<RecentRunViewModel> RecentRuns { get; } = [];

    [ObservableProperty]
    private bool _hasProfiles;

    public DashboardViewModel(ProfileService profileService, BackupOrchestrator orchestrator)
    {
        _profileService = profileService;
        _orchestrator = orchestrator;

        _orchestrator.BackupStarted += OnBackupStarted;
        _orchestrator.BackupCompleted += OnBackupCompleted;
    }

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

    [RelayCommand]
    public async Task DeleteProfileAsync(int profileId)
    {
        await _profileService.DeleteProfileAsync(profileId);
        await LoadAsync();
    }

    private void OnBackupStarted(object? sender, BackupStartedEventArgs e)
    {
        var card = Profiles.FirstOrDefault(p => p.ProfileId == e.Profile.Id);
        if (card == null) return;

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

public partial class ProfileCardViewModel : ObservableObject
{
    private readonly BackupOrchestrator _orchestrator;
    public int ProfileId { get; }
    public string Name { get; }
    public string VolumeGuid { get; }
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

    public string LastRunText => LastRunDate.HasValue
        ? FormatRelativeDate(LastRunDate.Value)
        : "Jamais sauvegardé";

    public string StatusIcon => LastRunStatus switch
    {
        BackupRunStatus.Success => "✓",
        BackupRunStatus.PartialSuccess => "⚠",
        BackupRunStatus.Error => "✗",
        BackupRunStatus.Cancelled => "—",
        _ => "?"
    };

    [RelayCommand]
    private async Task CancelAsync() => await _orchestrator.CancelBackupAsync(ProfileId);

    private static string FormatRelativeDate(DateTime date)
    {
        var diff = DateTime.UtcNow - date.ToUniversalTime();
        if (diff.TotalMinutes < 1) return "À l'instant";
        if (diff.TotalHours < 1) return $"il y a {(int)diff.TotalMinutes} min";
        if (diff.TotalDays < 1) return $"il y a {(int)diff.TotalHours}h";
        if (diff.TotalDays < 7) return $"il y a {(int)diff.TotalDays}j";
        return date.ToLocalTime().ToString("dd/MM/yyyy");
    }
}

public class RecentRunViewModel
{
    public string ProfileName { get; }
    public string DateText { get; }
    public string SummaryText { get; }
    public BackupRunStatus Status { get; }
    public bool IsSuccess => Status is BackupRunStatus.Success or BackupRunStatus.PartialSuccess;

    public RecentRunViewModel(BackupRun run)
    {
        ProfileName = run.Profile?.Name ?? "?";
        DateText = run.StartedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        Status = run.Status;
        SummaryText = run.TotalFiles == 0
            ? "Aucun changement"
            : $"+{run.FilesAdded} ~{run.FilesModified} -{run.FilesDeleted}";
    }
}
