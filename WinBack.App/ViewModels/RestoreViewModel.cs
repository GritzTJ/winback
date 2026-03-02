using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinBack.Core.Services;

namespace WinBack.App.ViewModels;

/// <summary>
/// ViewModel de la fenêtre de restauration.
/// Permet à l'utilisateur de choisir le dossier source (sauvegarde),
/// le dossier destination, d'indiquer si les fichiers sont chiffrés,
/// de saisir le mot de passe si nécessaire, et de lancer la restauration.
/// </summary>
public partial class RestoreViewModel : ViewModelBase
{
    private readonly RestoreEngine _restoreEngine;

    // ── Dossiers ─────────────────────────────────────────────────────────────

    /// <summary>Chemin du dossier de sauvegarde à restaurer (ex : D:\Documents).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRestoreCommand))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private string _sourceFolder = string.Empty;

    /// <summary>Chemin du dossier de destination (ex : C:\Users\tata\Desktop\Documents).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRestoreCommand))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private string _destinationFolder = string.Empty;

    // ── Chiffrement ───────────────────────────────────────────────────────────

    /// <summary>Vrai si les fichiers source sont chiffrés (format WinBack AES-256).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPasswordField))]
    [NotifyCanExecuteChangedFor(nameof(StartRestoreCommand))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private bool _isEncrypted = false;

    /// <summary>Mot de passe saisi par l'utilisateur (non persisté, effacé après usage).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRestoreCommand))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private string _password = string.Empty;

    /// <summary>Vrai si le champ mot de passe doit être affiché.</summary>
    public bool ShowPasswordField => IsEncrypted;

    // ── Options ───────────────────────────────────────────────────────────────

    /// <summary>Si vrai, écrase les fichiers existants à la destination.</summary>
    [ObservableProperty]
    private bool _overwrite = true;

    // ── Résultat ─────────────────────────────────────────────────────────────

    /// <summary>Résumé du résultat affiché après la restauration.</summary>
    [ObservableProperty]
    private string _resultText = string.Empty;

    /// <summary>Vrai après une restauration terminée avec succès (aucune erreur).</summary>
    [ObservableProperty]
    private bool _resultIsSuccess;

    /// <summary>Vrai si le panneau de résultat est visible.</summary>
    [ObservableProperty]
    private bool _showResult;

    /// <summary>Conditions nécessaires pour activer le bouton "Restaurer".</summary>
    public bool CanStart =>
        !string.IsNullOrWhiteSpace(SourceFolder) &&
        !string.IsNullOrWhiteSpace(DestinationFolder) &&
        (!IsEncrypted || !string.IsNullOrWhiteSpace(Password));

    public RestoreViewModel(RestoreEngine restoreEngine)
    {
        _restoreEngine = restoreEngine;
    }

    // ── Commandes ─────────────────────────────────────────────────────────────

    /// <summary>Lance la restauration selon les paramètres saisis.</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartRestoreAsync(CancellationToken ct)
    {
        ShowResult = false;
        SetBusy(true, "Restauration en cours…");

        try
        {
            byte[]? key = null;
            if (IsEncrypted)
            {
                // Dériver la clé à partir du mot de passe — même algorithme que la sauvegarde
                key = RestoreEngine.DeriveKey(Password);
                // Effacer le mot de passe de la mémoire dès que possible
                Password = string.Empty;
            }

            var options = new RestoreEngine.RestoreOptions(
                SourceFolder: SourceFolder,
                DestinationFolder: DestinationFolder,
                IsEncrypted: IsEncrypted,
                DecryptionKey: key,
                Overwrite: Overwrite);

            var progress = new Progress<RestoreEngine.RestoreProgress>(p =>
            {
                StatusMessage = $"{p.FilesProcessed}/{p.TotalFiles} — {p.CurrentFile}";
            });

            var result = await _restoreEngine.RestoreAsync(options, progress, ct);

            // Formater le résultat
            ResultIsSuccess = result.Errored == 0;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Restauration terminée — {result.Total} fichier(s)");
            sb.AppendLine();
            sb.AppendLine($"✓ Restaurés : {result.Restored}");
            if (result.Skipped > 0)
                sb.AppendLine($"  Ignorés   : {result.Skipped}  (fichiers existants)");
            if (result.Errored > 0)
            {
                sb.AppendLine($"✗ Erreurs   : {result.Errored}");
                foreach (var err in result.Errors.Take(5))
                    sb.AppendLine($"  • {err}");
                if (result.Errors.Count > 5)
                    sb.AppendLine($"  … et {result.Errors.Count - 5} autre(s)");
            }

            ResultText = sb.ToString().TrimEnd();
            ShowResult = true;
            StatusMessage = string.Empty;
        }
        catch (OperationCanceledException)
        {
            ResultText = "Restauration annulée.";
            ResultIsSuccess = false;
            ShowResult = true;
        }
        catch (Exception ex)
        {
            ResultText = $"Erreur : {ex.Message}";
            ResultIsSuccess = false;
            ShowResult = true;
        }
        finally
        {
            SetBusy(false);
        }
    }
}
