using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using WinBack.Core.Services;

namespace WinBack.App.ViewModels;

/// <summary>
/// ViewModel de la fenêtre de restauration.
/// <para>
/// Flux d'utilisation :
/// 1. L'utilisateur sélectionne le dossier source → l'arborescence charge automatiquement.
/// 2. L'utilisateur coche/décoche les fichiers/dossiers à restaurer.
/// 3. L'utilisateur sélectionne le dossier destination et configure les options.
/// 4. Clic sur "Restaurer" → seuls les fichiers sélectionnés sont copiés/déchiffrés.
/// </para>
/// </summary>
public partial class RestoreViewModel : ViewModelBase
{
    private readonly RestoreEngine _restoreEngine;
    private byte[]? _kdfSalt;

    // ── Dossiers ─────────────────────────────────────────────────────────────

    /// <summary>Chemin du dossier de sauvegarde à restaurer.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRestoreCommand))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private string _sourceFolder = string.Empty;

    /// <summary>Chemin du dossier de destination sur cette machine.</summary>
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

    // ── Arborescence de sélection ────────────────────────────────────────────

    /// <summary>
    /// Nœuds racines de l'arborescence du dossier source.
    /// Chargés automatiquement lorsque <see cref="SourceFolder"/> est défini.
    /// </summary>
    public ObservableCollection<FileTreeNode> FileTree { get; } = new();

    /// <summary>Vrai pendant le chargement de l'arborescence.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRestoreCommand))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(HasFileTree))]
    private bool _isLoadingTree;

    /// <summary>Vrai si l'arborescence est chargée et non vide.</summary>
    public bool HasFileTree => !IsLoadingTree && FileTree.Count > 0;

    /// <summary>
    /// Résumé textuel de la sélection (ex : "42 / 100 fichier(s) sélectionné(s)").
    /// Mis à jour chaque fois que la sélection change.
    /// </summary>
    public string SelectionSummary
    {
        get
        {
            if (FileTree.Count == 0) return string.Empty;
            var (selected, total) = CountFiles();
            return selected == total
                ? $"Tous les fichiers sélectionnés ({total})"
                : $"{selected} / {total} fichier(s) sélectionné(s)";
        }
    }

    // ── Résultat ─────────────────────────────────────────────────────────────

    /// <summary>Résumé du résultat affiché après la restauration.</summary>
    [ObservableProperty]
    private string _resultText = string.Empty;

    /// <summary>Vrai si la restauration s'est terminée sans erreur.</summary>
    [ObservableProperty]
    private bool _resultIsSuccess;

    /// <summary>Vrai si le panneau de résultat est visible.</summary>
    [ObservableProperty]
    private bool _showResult;

    // ── Condition d'activation du bouton Restaurer ────────────────────────────

    /// <summary>
    /// Vrai si toutes les conditions sont remplies pour lancer la restauration :
    /// dossier source et destination renseignés, mot de passe si chiffrement activé,
    /// arborescence chargée et au moins un fichier sélectionné.
    /// </summary>
    public bool CanStart =>
        !string.IsNullOrWhiteSpace(SourceFolder) &&
        !string.IsNullOrWhiteSpace(DestinationFolder) &&
        (!IsEncrypted || !string.IsNullOrWhiteSpace(Password)) &&
        !IsLoadingTree &&
        FileTree.Count > 0 &&
        CountFiles().selected > 0;

    // ── Constructeur ─────────────────────────────────────────────────────────

    public RestoreViewModel(RestoreEngine restoreEngine)
    {
        _restoreEngine = restoreEngine;

        // Mettre à jour HasFileTree, SelectionSummary et CanStart quand
        // des nœuds sont ajoutés/retirés de la collection racine.
        FileTree.CollectionChanged += OnFileTreeCollectionChanged;
    }

    /// <summary>
    /// Libère les abonnements aux événements pour éviter les fuites mémoire.
    /// Appelé par RestoreWindow lorsqu'elle se ferme.
    /// </summary>
    public void Cleanup()
    {
        ClearFileTree();
        FileTree.CollectionChanged -= OnFileTreeCollectionChanged;
    }

    // ── Réaction aux changements de SourceFolder ──────────────────────────────

    /// <summary>
    /// Déclenché automatiquement quand SourceFolder change.
    /// Vide l'arborescence existante et lance le rechargement asynchrone.
    /// </summary>
    partial void OnSourceFolderChanged(string value)
    {
        ClearFileTree();
        _kdfSalt = null;
        if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
        {
            // Charger les métadonnées KDF si présentes (PBKDF2 v2)
            TryLoadKdfMetadata(value);
            _ = LoadFileTreeAsync(value);
        }
    }

    private void TryLoadKdfMetadata(string sourceFolder)
    {
        try
        {
            var kdfFile = Path.Combine(sourceFolder, RestoreEngine.KdfMetadataFileName);
            if (!File.Exists(kdfFile)) return;
            var json = File.ReadAllText(kdfFile);
            var meta = JsonSerializer.Deserialize<RestoreEngine.KdfMetadata>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (meta != null && meta.KdfVersion >= 2 && !string.IsNullOrEmpty(meta.Salt))
                _kdfSalt = Convert.FromBase64String(meta.Salt);
        }
        catch { /* Métadonnées KDF corrompues ou inaccessibles — utiliser le KDF legacy */ }
    }

    // ── Commandes ─────────────────────────────────────────────────────────────

    /// <summary>Sélectionne tous les fichiers de l'arborescence.</summary>
    [RelayCommand]
    private void SelectAll()
    {
        foreach (var node in FileTree)
            node.SetIsChecked(true, propagateDown: true, propagateUp: false);
        // Notifier la sélection manuellement (pas de propagation vers parent = pas de notification auto)
        RefreshSelectionUI();
    }

    /// <summary>Désélectionne tous les fichiers de l'arborescence.</summary>
    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var node in FileTree)
            node.SetIsChecked(false, propagateDown: true, propagateUp: false);
        RefreshSelectionUI();
    }

    /// <summary>Lance la restauration selon les paramètres et la sélection.</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartRestoreAsync(CancellationToken ct)
    {
        ShowResult = false;
        SetBusy(true, "Restauration en cours…");

        try
        {
            // Dériver la clé AES-256 si chiffrement activé, puis effacer le mot de passe
            byte[]? key = null;
            if (IsEncrypted)
            {
                key = _kdfSalt != null
                    ? RestoreEngine.DeriveKeyV2(Password, _kdfSalt)
                    : RestoreEngine.DeriveKey(Password);
                Password = string.Empty;
            }

            // Construire l'ensemble des chemins sélectionnés pour la restauration sélective.
            // HashSet avec OrdinalIgnoreCase pour une correspondance robuste sur Windows.
            HashSet<string>? includedPaths = null;
            if (FileTree.Count > 0)
            {
                includedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var node in FileTree)
                    foreach (var path in node.GetSelectedRelativePaths())
                        includedPaths.Add(path);
            }

            var options = new RestoreEngine.RestoreOptions(
                SourceFolder: SourceFolder,
                DestinationFolder: DestinationFolder,
                IsEncrypted: IsEncrypted,
                DecryptionKey: key,
                Overwrite: Overwrite,
                IncludedPaths: includedPaths);

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
            if (key != null) Array.Clear(key);
            SetBusy(false);
        }
    }

    // ── Chargement de l'arborescence ─────────────────────────────────────────

    /// <summary>
    /// Charge asynchronement l'arborescence du dossier source.
    /// La construction de l'arbre est effectuée en arrière-plan (Task.Run)
    /// pour ne pas bloquer le thread UI.
    /// </summary>
    private async Task LoadFileTreeAsync(string sourceFolder)
    {
        IsLoadingTree = true;
        try
        {
            List<FileTreeNode> roots;
            try
            {
                roots = await Task.Run(() => BuildTree(sourceFolder));
            }
            catch (Exception ex)
            {
                // Dossier inaccessible ou erreur I/O : afficher le résultat vide
                _ = ex; // loggé implicitement via le résultat vide
                IsLoadingTree = false;
                return;
            }

            // Ajouter les nœuds sur le thread UI et s'abonner à leurs changements
            foreach (var node in roots)
            {
                node.PropertyChanged += OnRootNodePropertyChanged;
                FileTree.Add(node);
            }
        }
        finally
        {
            IsLoadingTree = false;
        }
    }

    /// <summary>
    /// Construit l'arborescence complète d'un dossier de manière récursive.
    /// Dossiers en premier (triés), puis fichiers (triés).
    /// Le dossier <c>.winback_recycle</c> est exclu (corbeille interne WinBack).
    /// </summary>
    private static List<FileTreeNode> BuildTree(string rootPath)
    {
        var roots = new List<FileTreeNode>();
        BuildTreeRecursive(rootPath, rootPath, parent: null, target: roots);
        return roots;
    }

    private static void BuildTreeRecursive(
        string rootPath, string currentPath,
        FileTreeNode? parent, IList<FileTreeNode> target)
    {
        // Dossiers d'abord (triés par nom, insensible à la casse)
        IEnumerable<string> directories;
        try { directories = Directory.EnumerateDirectories(currentPath); }
        catch (UnauthorizedAccessException) { return; } // Dossier non accessible
        catch (IOException) { return; } // Dossier disparu pendant l'énumération

        foreach (var dir in directories
                    .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(dir);

            // Ignorer la corbeille interne WinBack
            if (string.Equals(name, ".winback_recycle", StringComparison.OrdinalIgnoreCase))
                continue;

            var relPath = Path.GetRelativePath(rootPath, dir);
            var node = new FileTreeNode(name, relPath, isDirectory: true, sizeBytes: 0, parent);
            BuildTreeRecursive(rootPath, dir, node, node.Children);
            target.Add(node);
        }

        // Fichiers ensuite (triés par nom)
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(currentPath); }
        catch (UnauthorizedAccessException) { return; }
        catch (IOException) { return; }

        foreach (var file in files
                    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var name = Path.GetFileName(file);
                var relPath = Path.GetRelativePath(rootPath, file);
                var size = new FileInfo(file).Length;
                target.Add(new FileTreeNode(name, relPath, isDirectory: false, sizeBytes: size, parent));
            }
            catch (IOException) { /* Fichier inaccessible, on l'ignore */ }
        }
    }

    // ── Gestion de la sélection ───────────────────────────────────────────────

    /// <summary>
    /// Appelé quand un nœud racine change son état IsChecked.
    /// Comme la propagation est ascendante jusqu'à la racine, ce handler est
    /// déclenché lors de tout changement dans le sous-arbre (y compris les feuilles).
    /// </summary>
    private void OnRootNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FileTreeNode.IsChecked)) return;
        RefreshSelectionUI();
    }

    /// <summary>
    /// Notifie l'UI que la sélection a changé (résumé + état du bouton Restaurer).
    /// </summary>
    private void RefreshSelectionUI()
    {
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(CanStart));
        StartRestoreCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Met à jour HasFileTree, SelectionSummary et CanStart quand
    /// des nœuds sont ajoutés ou retirés de la collection racine.
    /// </summary>
    private void OnFileTreeCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasFileTree));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(CanStart));
        StartRestoreCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Vide l'arborescence et se désabonne des événements des nœuds racines
    /// pour éviter les fuites mémoire.
    /// </summary>
    private void ClearFileTree()
    {
        foreach (var node in FileTree)
            node.PropertyChanged -= OnRootNodePropertyChanged;
        FileTree.Clear();
        ShowResult = false;
    }

    // ── Comptage des fichiers ─────────────────────────────────────────────────

    /// <summary>
    /// Retourne le nombre de fichiers sélectionnés et le total.
    /// Parcourt l'arbre une seule fois pour les deux valeurs.
    /// </summary>
    private (int selected, int total) CountFiles()
    {
        int selected = 0, total = 0;
        foreach (var node in FileTree)
            CountFilesRecursive(node, ref selected, ref total);
        return (selected, total);
    }

    private static void CountFilesRecursive(FileTreeNode node, ref int selected, ref int total)
    {
        if (!node.IsDirectory)
        {
            total++;
            if (node.IsChecked == true) selected++;
            return;
        }
        foreach (var child in node.Children)
            CountFilesRecursive(child, ref selected, ref total);
    }
}
