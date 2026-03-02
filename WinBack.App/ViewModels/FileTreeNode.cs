using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace WinBack.App.ViewModels;

/// <summary>
/// Nœud de l'arborescence de sélection pour la restauration sélective.
/// Représente un fichier ou un dossier du dossier source de sauvegarde.
///
/// Supporte un état tri-state (coché / décoché / indéterminé) :
/// - <c>true</c>  : sélectionné (sera restauré)
/// - <c>false</c> : désélectionné (sera ignoré)
/// - <c>null</c>  : partiellement sélectionné (dossiers uniquement, calculé automatiquement)
///
/// La propagation est bidirectionnelle :
/// - Cocher/décocher un dossier applique l'état à tous ses descendants.
/// - Modifier un enfant met à jour l'état indéterminé des ancêtres.
/// </summary>
public class FileTreeNode : ObservableObject
{
    // Valeur interne — ne pas utiliser [ObservableProperty] car la propagation
    // nécessite un contrôle fin (éviter les cycles récursifs).
    private bool? _isChecked = true;

    // ── Identité ─────────────────────────────────────────────────────────────

    /// <summary>Nom affiché (sans chemin) : nom de fichier ou de dossier.</summary>
    public string Name { get; }

    /// <summary>
    /// Chemin relatif par rapport à la racine du dossier source.
    /// Utilisé pour le filtrage dans <see cref="WinBack.Core.Services.RestoreEngine"/>.
    /// </summary>
    public string RelativePath { get; }

    /// <summary>Vrai si ce nœud représente un dossier (a des enfants).</summary>
    public bool IsDirectory { get; }

    /// <summary>Taille du fichier en octets (0 pour les dossiers).</summary>
    public long SizeBytes { get; }

    // ── Navigation ───────────────────────────────────────────────────────────

    /// <summary>Nœud parent dans l'arborescence (<c>null</c> si nœud racine).</summary>
    public FileTreeNode? Parent { get; }

    /// <summary>Enfants directs (sous-dossiers et fichiers du répertoire).</summary>
    public ObservableCollection<FileTreeNode> Children { get; } = new();

    // ── Propriétés d'affichage ────────────────────────────────────────────────

    /// <summary>Icône textuelle devant le nom : 📁 pour dossiers, 📄 pour fichiers.</summary>
    public string Icon => IsDirectory ? "📁" : "📄";

    /// <summary>
    /// Taille formatée lisible (ex : "1,2 Mo").
    /// Retourne une chaîne vide pour les dossiers.
    /// </summary>
    public string FormattedSize => IsDirectory ? string.Empty : FormatSize(SizeBytes);

    // ── État de sélection ─────────────────────────────────────────────────────

    /// <summary>
    /// État de sélection tri-state :
    /// <c>true</c> = sélectionné, <c>false</c> = désélectionné,
    /// <c>null</c> = partiellement sélectionné (indéterminé).
    /// <para>
    /// Lorsqu'un CheckBox WPF en état indéterminé est cliqué avec <c>IsThreeState="True"</c>,
    /// WPF envoie <c>null</c> via le binding — le setter le convertit en <c>true</c>
    /// (comportement « cliquer sur indéterminé = tout sélectionner »).
    /// </para>
    /// </summary>
    public bool? IsChecked
    {
        get => _isChecked;
        // null envoyé par WPF (clic sur indéterminé) → convertit en true (tout sélectionner)
        set => SetIsChecked(value ?? true, propagateDown: true, propagateUp: true);
    }

    // ── Constructeur ─────────────────────────────────────────────────────────

    /// <param name="name">Nom d'affichage (sans chemin).</param>
    /// <param name="relativePath">Chemin relatif par rapport à la racine source.</param>
    /// <param name="isDirectory">Vrai pour un dossier.</param>
    /// <param name="sizeBytes">Taille en octets (0 pour les dossiers).</param>
    /// <param name="parent">Nœud parent (<c>null</c> si racine).</param>
    public FileTreeNode(
        string name, string relativePath, bool isDirectory,
        long sizeBytes, FileTreeNode? parent)
    {
        Name = name;
        RelativePath = relativePath;
        IsDirectory = isDirectory;
        SizeBytes = sizeBytes;
        Parent = parent;
    }

    // ── Méthodes publiques ───────────────────────────────────────────────────

    /// <summary>
    /// Modifie l'état de sélection avec contrôle explicite de la propagation.
    /// Utilisé en interne et par les commandes "Tout sélectionner" / "Tout désélectionner".
    /// </summary>
    /// <param name="value">Nouvelle valeur (<c>null</c> → indéterminé, uniquement en interne).</param>
    /// <param name="propagateDown">Applique l'état à tous les descendants (uniquement si <paramref name="value"/> est non-null).</param>
    /// <param name="propagateUp">Met à jour l'état des ancêtres.</param>
    public void SetIsChecked(bool? value, bool propagateDown, bool propagateUp)
    {
        if (_isChecked == value) return;

        _isChecked = value;
        OnPropertyChanged(nameof(IsChecked));

        // Propager vers les enfants (uniquement si état binaire — pas l'état indéterminé)
        if (propagateDown && value.HasValue)
        {
            foreach (var child in Children)
                child.SetIsChecked(value, propagateDown: true, propagateUp: false);
        }

        // Remonter l'état vers le parent
        if (propagateUp)
            Parent?.UpdateCheckedStateFromChildren();
    }

    /// <summary>
    /// Retourne les chemins relatifs de tous les fichiers <b>sélectionnés</b>
    /// dans ce sous-arbre (ce nœud et tous ses descendants).
    /// Ne retourne jamais les dossiers eux-mêmes, uniquement les fichiers.
    /// </summary>
    public IEnumerable<string> GetSelectedRelativePaths()
    {
        if (!IsDirectory)
        {
            // Fichier : retourner son chemin s'il est sélectionné
            if (_isChecked == true)
                yield return RelativePath;
        }
        else
        {
            // Dossier : déléguer aux enfants
            foreach (var child in Children)
                foreach (var path in child.GetSelectedRelativePaths())
                    yield return path;
        }
    }

    // ── Méthodes internes ─────────────────────────────────────────────────────

    /// <summary>
    /// Recalcule l'état de ce dossier en fonction de l'état de ses enfants directs,
    /// puis propage la mise à jour vers le nœud parent.
    /// Appelé automatiquement lors de modifications d'un enfant.
    /// </summary>
    internal void UpdateCheckedStateFromChildren()
    {
        if (Children.Count == 0) return;

        // Déterminer si tous les enfants ont le même état
        bool? unified = Children[0]._isChecked;
        for (int i = 1; i < Children.Count; i++)
        {
            if (Children[i]._isChecked != unified)
            {
                unified = null; // États hétérogènes → indéterminé
                break;
            }
        }

        if (_isChecked == unified) return;

        _isChecked = unified;
        OnPropertyChanged(nameof(IsChecked));

        // Continuer la remontée vers la racine
        Parent?.UpdateCheckedStateFromChildren();
    }

    // ── Helpers privés ────────────────────────────────────────────────────────

    /// <summary>Formate une taille en octets en chaîne lisible.</summary>
    private static string FormatSize(long bytes) => bytes switch
    {
        < 1_024 => $"{bytes} o",
        < 1_024 * 1_024 => $"{bytes / 1_024.0:F1} Ko",
        < 1_024L * 1_024 * 1_024 => $"{bytes / (1_024.0 * 1_024):F1} Mo",
        _ => $"{bytes / (1_024.0 * 1_024 * 1_024):F1} Go"
    };
}
