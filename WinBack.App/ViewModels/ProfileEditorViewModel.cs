using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using WinBack.Core.Models;
using WinBack.Core.Services;

namespace WinBack.App.ViewModels;

/// <summary>
/// ViewModel pour la création/édition d'un profil de sauvegarde.
/// Fonctionne en mode assistant (4 étapes) pour la création,
/// et en mode formulaire direct pour l'édition.
/// </summary>
public partial class ProfileEditorViewModel : ViewModelBase
{
    private readonly ProfileService _profileService;

    // ── Wizard steps ────────────────────────────────────────────────────────
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsStep1))]
    [NotifyPropertyChangedFor(nameof(IsStep2))][NotifyPropertyChangedFor(nameof(IsStep3))]
    [NotifyPropertyChangedFor(nameof(IsStep4))][NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(NextButtonText))]
    private int _currentStep = 1;

    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;
    public bool IsStep4 => CurrentStep == 4;
    public bool CanGoBack => CurrentStep > 1;
    public string NextButtonText => CurrentStep < 4 ? "Suivant →" : "Enregistrer";

    // ── Étape 1 : Identification ─────────────────────────────────────────────
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(NextStepCommand))]
    private string _profileName = string.Empty;

    [ObservableProperty]
    private string _detectedDriveLabel = string.Empty;

    [ObservableProperty]
    private string _detectedVolumeGuid = string.Empty;

    [ObservableProperty]
    private string _detectedDriveLetter = string.Empty;

    // ── Étape 2 : Dossiers sources ───────────────────────────────────────────
    public ObservableCollection<PairRowViewModel> Pairs { get; } = [];

    // ── Étape 3 : Options ────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecycleBinStrategy))]
    private BackupStrategy selectedStrategy = BackupStrategy.Mirror;

    /// <summary>Utilisé pour afficher/masquer le champ de rétention en XAML.</summary>
    public bool IsRecycleBinStrategy => SelectedStrategy == BackupStrategy.RecycleBin;

    [ObservableProperty]
    private int retentionDays = 30;

    [ObservableProperty]
    private bool autoStart = true;

    [ObservableProperty]
    private bool enableVss = true;

    [ObservableProperty]
    private bool enableHashVerification = false;

    [ObservableProperty]
    private int insertionDelaySeconds = 3;

    // ── Étape 4 : Récapitulatif / mode édition ───────────────────────────────
    [ObservableProperty]
    private bool _isEditMode;

    private int _editingProfileId;

    // ── Résultat ─────────────────────────────────────────────────────────────
    public bool Saved { get; private set; }
    public BackupProfile? SavedProfile { get; private set; }

    public ProfileEditorViewModel(ProfileService profileService)
    {
        _profileService = profileService;
    }

    /// <summary>Pré-remplir avec un disque détecté (mode création depuis détection USB).</summary>
    public void InitFromDrive(DriveDetails drive)
    {
        DetectedDriveLabel = drive.Label;
        DetectedVolumeGuid = drive.VolumeGuid;
        DetectedDriveLetter = drive.DriveLetter;
        ProfileName = drive.Label;

        // Ajouter une paire par défaut avec comme destination le dossier "Sauvegarde"
        if (Pairs.Count == 0)
            Pairs.Add(new PairRowViewModel { DestRelativePath = "Sauvegarde" });
    }

    /// <summary>Charger un profil existant pour édition.</summary>
    public void InitFromProfile(BackupProfile profile)
    {
        IsEditMode = true;
        _editingProfileId = profile.Id;
        ProfileName = profile.Name;
        DetectedVolumeGuid = profile.VolumeGuid;
        DetectedDriveLabel = profile.DiskLabel ?? profile.VolumeGuid;
        SelectedStrategy = profile.Strategy;
        RetentionDays = profile.RetentionDays;
        AutoStart = profile.AutoStart;
        EnableVss = profile.EnableVss;
        EnableHashVerification = profile.EnableHashVerification;
        InsertionDelaySeconds = profile.InsertionDelaySeconds;

        Pairs.Clear();
        foreach (var pair in profile.Pairs.Where(p => p.IsActive))
            Pairs.Add(new PairRowViewModel
            {
                Id = pair.Id,
                SourcePath = pair.SourcePath,
                DestRelativePath = pair.DestRelativePath,
                ExcludePatterns = string.Join(";", pair.ExcludePatterns)
            });

        CurrentStep = IsEditMode ? 1 : 1;
    }

    [RelayCommand(CanExecute = nameof(CanGoToNextStep))]
    private async Task NextStepAsync()
    {
        if (CurrentStep < 4)
            CurrentStep++;
        else
            await SaveAsync();
    }

    private bool CanGoToNextStep() =>
        CurrentStep switch
        {
            1 => !string.IsNullOrWhiteSpace(ProfileName) && !string.IsNullOrWhiteSpace(DetectedVolumeGuid),
            2 => Pairs.Count > 0 && Pairs.All(p => !string.IsNullOrWhiteSpace(p.SourcePath)),
            _ => true
        };

    [RelayCommand]
    private void PreviousStep()
    {
        if (CurrentStep > 1) CurrentStep--;
    }

    [RelayCommand]
    private void AddPair()
    {
        Pairs.Add(new PairRowViewModel());
        NextStepCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void RemovePair(PairRowViewModel pair)
    {
        Pairs.Remove(pair);
        NextStepCommand.NotifyCanExecuteChanged();
    }

    private async Task SaveAsync()
    {
        SetBusy(true, "Enregistrement…");
        try
        {
            if (IsEditMode)
            {
                var existing = new BackupProfile
                {
                    Id = _editingProfileId,
                    Name = ProfileName,
                    VolumeGuid = DetectedVolumeGuid,
                    DiskLabel = DetectedDriveLabel,
                    Strategy = SelectedStrategy,
                    RetentionDays = RetentionDays,
                    AutoStart = AutoStart,
                    EnableVss = EnableVss,
                    EnableHashVerification = EnableHashVerification,
                    InsertionDelaySeconds = InsertionDelaySeconds,
                    Pairs = Pairs.Select(p => new BackupPair
                    {
                        Id = p.Id,
                        ProfileId = _editingProfileId,
                        SourcePath = p.SourcePath,
                        DestRelativePath = p.DestRelativePath,
                        ExcludePatterns = p.ExcludePatterns
                            .Split(';', StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim())
                            .ToList()
                    }).ToList()
                };
                await _profileService.UpdateProfileAsync(existing);
                SavedProfile = existing;
            }
            else
            {
                var profile = new BackupProfile
                {
                    Name = ProfileName,
                    VolumeGuid = DetectedVolumeGuid,
                    DiskLabel = DetectedDriveLabel,
                    Strategy = SelectedStrategy,
                    RetentionDays = RetentionDays,
                    AutoStart = AutoStart,
                    EnableVss = EnableVss,
                    EnableHashVerification = EnableHashVerification,
                    InsertionDelaySeconds = InsertionDelaySeconds,
                    Pairs = Pairs.Select(p => new BackupPair
                    {
                        SourcePath = p.SourcePath,
                        DestRelativePath = p.DestRelativePath,
                        ExcludePatterns = p.ExcludePatterns
                            .Split(';', StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim())
                            .ToList()
                    }).ToList()
                };
                SavedProfile = await _profileService.CreateProfileAsync(profile);
            }
            Saved = true;
        }
        finally { SetBusy(false); }
    }
}

public partial class PairRowViewModel : ObservableObject
{
    public int Id { get; set; }

    [ObservableProperty]
    private string _sourcePath = string.Empty;

    [ObservableProperty]
    private string _destRelativePath = string.Empty;

    /// <summary>Patterns d'exclusion séparés par ";" ex: *.tmp;~$*;.git</summary>
    [ObservableProperty]
    private string _excludePatterns = "*.tmp;~$*;Thumbs.db;desktop.ini";
}
