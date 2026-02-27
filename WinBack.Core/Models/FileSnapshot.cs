namespace WinBack.Core.Models;

/// <summary>
/// Représente l'état connu d'un fichier au moment de la dernière sauvegarde réussie.
/// Utilisé pour calculer le diff lors de la sauvegarde suivante.
/// </summary>
public class FileSnapshot
{
    public int ProfileId { get; set; }
    public int PairId { get; set; }

    /// <summary>Chemin relatif au dossier source, ex: Sous-Dossier\fichier.pdf</summary>
    public string RelativePath { get; set; } = string.Empty;

    public long Size { get; set; }
    public DateTime LastModified { get; set; }

    /// <summary>Hash MD5 du fichier (optionnel, calculé si EnableHashVerification = true).</summary>
    public string? Hash { get; set; }

    public BackupProfile Profile { get; set; } = null!;
    public BackupPair Pair { get; set; } = null!;
}
