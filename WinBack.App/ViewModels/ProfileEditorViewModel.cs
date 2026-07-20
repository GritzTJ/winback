using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
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
    // NotifyCanExecuteChangedFor est indispensable : les conditions de CanGoToNextStep
    // dépendent de l'étape courante. Sans lui, l'état CanExecute calculé à l'étape 1
    // resterait actif à l'étape 2 et permettrait de valider des paires incomplètes.
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsStep1))]
    [NotifyPropertyChangedFor(nameof(IsStep2))][NotifyPropertyChangedFor(nameof(IsStep3))]
    [NotifyPropertyChangedFor(nameof(IsStep4))][NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(NextButtonText))]
    [NotifyCanExecuteChangedFor(nameof(NextStepCommand))]
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

    /// <summary>
    /// Chiffrement AES-256 activé pour ce profil.
    /// Le mot de passe sera demandé lors de chaque connexion du disque (non stocké en base).
    /// </summary>
    [ObservableProperty]
    private bool enableEncryption = false;

    [ObservableProperty]
    private int insertionDelaySeconds = 3;

    // ── Étape 4 : Récapitulatif / mode édition ───────────────────────────────
    [ObservableProperty]
    private bool _isEditMode;

    private int _editingProfileId;

    /// <summary>
    /// Sel PBKDF2 du profil en cours d'édition, conservé tel quel.
    /// Il n'est pas modifiable par l'utilisateur mais doit être réinjecté à
    /// l'enregistrement : le perdre rendrait indéchiffrables toutes les
    /// sauvegardes existantes du profil.
    /// </summary>
    private string? _editingEncryptionSalt;

    /// <summary>Numéro de série du disque, conservé à l'identique en édition (informatif).</summary>
    private string? _editingDiskSerialNumber;

    // ── Résultat ─────────────────────────────────────────────────────────────
    public bool Saved { get; private set; }
    public BackupProfile? SavedProfile { get; private set; }

    public ProfileEditorViewModel(ProfileService profileService)
    {
        _profileService = profileService;

        // La validité de l'étape 2 dépend du contenu des paires : il faut réévaluer
        // CanExecute quand une paire est ajoutée/retirée ET quand un champ est modifié
        // (sinon le bouton « Suivant » reste grisé après le choix d'un dossier source).
        Pairs.CollectionChanged += OnPairsCollectionChanged;
    }

    private void OnPairsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var item in e.OldItems?.OfType<PairRowViewModel>() ?? [])
            item.PropertyChanged -= OnPairRowChanged;
        foreach (var item in e.NewItems?.OfType<PairRowViewModel>() ?? [])
            item.PropertyChanged += OnPairRowChanged;

        NextStepCommand.NotifyCanExecuteChanged();
    }

    private void OnPairRowChanged(object? sender, PropertyChangedEventArgs e)
        => NextStepCommand.NotifyCanExecuteChanged();

    /// <summary>
    /// Désabonne les événements des lignes de paires.
    /// Appelé par <c>ProfileEditorWindow</c> à la fermeture.
    /// </summary>
    public void Cleanup()
    {
        foreach (var pair in Pairs)
            pair.PropertyChanged -= OnPairRowChanged;
        Pairs.CollectionChanged -= OnPairsCollectionChanged;
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
        _editingEncryptionSalt   = profile.EncryptionSalt;
        _editingDiskSerialNumber = profile.DiskSerialNumber;
        ProfileName = profile.Name;
        DetectedVolumeGuid = profile.VolumeGuid;
        DetectedDriveLabel = profile.DiskLabel ?? profile.VolumeGuid;
        SelectedStrategy = profile.Strategy;
        RetentionDays = profile.RetentionDays;
        AutoStart = profile.AutoStart;
        EnableVss = profile.EnableVss;
        EnableHashVerification = profile.EnableHashVerification;
        EnableEncryption = profile.EnableEncryption;
        // Le mot de passe de chiffrement n'est jamais stocké — il sera redemandé à la connexion.
        InsertionDelaySeconds = profile.InsertionDelaySeconds;

        // Toutes les paires sont chargées, y compris les inactives : UpdateProfileAsync
        // supprime celles qui ne sont pas renvoyées, donc filtrer ici les détruirait.
        Pairs.Clear();
        foreach (var pair in profile.Pairs)
            Pairs.Add(new PairRowViewModel
            {
                Id = pair.Id,
                IsActive = pair.IsActive,
                SourcePath = pair.SourcePath,
                DestRelativePath = pair.DestRelativePath,
                ExcludePatterns = string.Join(";", pair.ExcludePatterns)
            });

        CurrentStep = 1;
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
            2 => Pairs.Count > 0 && Pairs.All(p => ValidatePair(p) == null),
            _ => true
        };

    /// <summary>
    /// Valide une paire source → destination.
    /// Retourne <c>null</c> si la paire est valide, sinon le message d'erreur à afficher.
    /// Source unique de vérité, partagée par l'assistant (bouton « Suivant ») et par
    /// l'enregistrement : les deux ne peuvent donc pas diverger.
    /// </summary>
    private static string? ValidatePair(PairRowViewModel p)
    {
        if (string.IsNullOrWhiteSpace(p.SourcePath))
            return "Chaque dossier source doit être renseigné.";
        if (p.SourcePath.TrimStart().StartsWith(@"\\"))
            return $"Chemin UNC interdit : {p.SourcePath}";
        if (string.IsNullOrWhiteSpace(p.DestRelativePath))
            return "Chaque dossier de destination doit être renseigné.";
        if (p.DestRelativePath.Contains(".."))
            return $"Chemin invalide (path traversal) : {p.DestRelativePath}";
        if (Path.IsPathRooted(p.DestRelativePath))
            return $"Le chemin de destination doit être relatif : {p.DestRelativePath}";
        return null;
    }

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
        // Dernier rempart avant écriture en base : l'assistant ne doit jamais laisser
        // passer une paire invalide, mais on ne s'appuie pas sur l'UI pour la sécurité.
        if (Pairs.Count == 0)
        {
            StatusMessage = "Ajoutez au moins un dossier à sauvegarder.";
            return;
        }
        foreach (var p in Pairs)
        {
            var error = ValidatePair(p);
            if (error != null)
            {
                StatusMessage = error;
                return;
            }
        }

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
                    EnableEncryption = EnableEncryption,
                    InsertionDelaySeconds = InsertionDelaySeconds,
                    // Champs non éditables, restitués tels quels pour ne pas les écraser
                    EncryptionSalt   = _editingEncryptionSalt,
                    DiskSerialNumber = _editingDiskSerialNumber,
                    Pairs = Pairs.Select(p => new BackupPair
                    {
                        Id = p.Id,
                        ProfileId = _editingProfileId,
                        IsActive = p.IsActive,
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
                    EnableEncryption = EnableEncryption,
                    InsertionDelaySeconds = InsertionDelaySeconds,
                    Pairs = Pairs.Select(p => new BackupPair
                    {
                        IsActive = p.IsActive,
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

    /// <summary>
    /// Vrai si la paire est prise en compte lors des sauvegardes.
    /// Conservée à l'identique en édition : sans elle, toutes les paires
    /// désactivées seraient réactivées (ou supprimées) à l'enregistrement.
    /// </summary>
    public bool IsActive { get; set; } = true;

    [ObservableProperty]
    private string _sourcePath = string.Empty;

    [ObservableProperty]
    private string _destRelativePath = string.Empty;

    /// <summary>Patterns d'exclusion séparés par ";" ex: *.tmp;~$*;.git</summary>
    [ObservableProperty]
    private string _excludePatterns = "*.tmp;~$*;Thumbs.db;desktop.ini";
}
