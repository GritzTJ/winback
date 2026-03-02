namespace WinBack.Core.Services;

/// <summary>
/// DTO racine pour l'export/import d'un profil de sauvegarde au format JSON.
/// Le champ <see cref="FormatVersion"/> permet de gérer des évolutions futures
/// du format sans casser la compatibilité des fichiers existants.
/// </summary>
/// <param name="FormatVersion">Numéro de version du format d'export ("1" pour 0.4.x).</param>
/// <param name="Name">Nom convivial du profil.</param>
/// <param name="VolumeGuid">GUID du volume Windows — identifiant stable du disque cible.</param>
/// <param name="DiskLabel">Étiquette du volume au moment de l'export (informatif).</param>
/// <param name="Strategy">Stratégie de suppression : "Mirror", "RecycleBin" ou "Additive".</param>
/// <param name="RetentionDays">Durée de rétention en jours (corbeille de sauvegarde).</param>
/// <param name="InsertionDelaySeconds">Délai avant démarrage après insertion du disque.</param>
/// <param name="EnableHashVerification">Vérification d'intégrité après copie.</param>
/// <param name="EnableVss">Utilisation de VSS pour copier les fichiers ouverts.</param>
/// <param name="AutoStart">Démarrage automatique sans confirmation.</param>
/// <param name="EnableEncryption">Chiffrement AES-256 activé.</param>
/// <param name="Pairs">Paires source → destination du profil.</param>
public record ProfileExportDto(
    string FormatVersion,
    string Name,
    string VolumeGuid,
    string? DiskLabel,
    string Strategy,
    int RetentionDays,
    int InsertionDelaySeconds,
    bool EnableHashVerification,
    bool EnableVss,
    bool AutoStart,
    bool EnableEncryption,
    List<PairExportDto> Pairs);

/// <summary>
/// DTO d'une paire source → destination dans un profil exporté.
/// </summary>
/// <param name="SourcePath">Chemin absolu du dossier source (ex : C:\Users\Alice\Documents).</param>
/// <param name="DestRelativePath">Chemin relatif à la racine du disque de destination.</param>
/// <param name="ExcludePatternsJson">Patterns d'exclusion sérialisés en JSON (ex : ["*.tmp"]).</param>
/// <param name="IsActive">Vrai si la paire est active.</param>
public record PairExportDto(
    string SourcePath,
    string DestRelativePath,
    string ExcludePatternsJson,
    bool IsActive);
