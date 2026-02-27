namespace WinBack.Core.Models;

public enum BackupStrategy
{
    /// <summary>Miroir strict : les suppressions côté source sont répercutées sur la destination.</summary>
    Mirror,
    /// <summary>Les fichiers supprimés côté source sont déplacés dans une corbeille de sauvegarde.</summary>
    RecycleBin,
    /// <summary>Les suppressions ne sont jamais répercutées (accumulation).</summary>
    Additive
}

public class BackupProfile
{
    public int Id { get; set; }

    /// <summary>Nom convivial affiché à l'utilisateur, ex: "SSD Samsung Papa".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>GUID du volume Windows, ex: {xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}. Identifiant stable.</summary>
    public string VolumeGuid { get; set; } = string.Empty;

    /// <summary>Étiquette du volume au moment de la création du profil (informatif).</summary>
    public string? DiskLabel { get; set; }

    /// <summary>Numéro de série du disque physique (informatif, mode avancé).</summary>
    public string? DiskSerialNumber { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    public BackupStrategy Strategy { get; set; } = BackupStrategy.Mirror;

    /// <summary>Nombre de jours de rétention pour la corbeille de sauvegarde (si Strategy = RecycleBin).</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>Délai en secondes avant de démarrer la sauvegarde après insertion du disque.</summary>
    public int InsertionDelaySeconds { get; set; } = 3;

    /// <summary>Vérification d'intégrité par hash après copie (plus lent).</summary>
    public bool EnableHashVerification { get; set; } = false;

    /// <summary>Utiliser VSS pour copier les fichiers ouverts.</summary>
    public bool EnableVss { get; set; } = true;

    /// <summary>Démarrer la sauvegarde automatiquement sans confirmation.</summary>
    public bool AutoStart { get; set; } = true;

    public List<BackupPair> Pairs { get; set; } = [];
    public List<BackupRun> Runs { get; set; } = [];
    public List<FileSnapshot> Snapshots { get; set; } = [];
}
