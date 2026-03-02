using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace WinBack.Core.Services;

/// <summary>
/// Moteur de restauration : copie les fichiers d'un dossier de sauvegarde vers
/// une destination choisie par l'utilisateur.
/// Supporte les fichiers en clair et les fichiers chiffrés AES-256
/// (format WinBack : 16 octets IV en tête de fichier, données chiffrées CBC).
/// </summary>
public class RestoreEngine
{
    private readonly ILogger<RestoreEngine> _logger;

    public RestoreEngine(ILogger<RestoreEngine> logger)
    {
        _logger = logger;
    }

    // ── Types ────────────────────────────────────────────────────────────────

    /// <summary>Paramètres d'une opération de restauration.</summary>
    /// <param name="SourceFolder">Dossier contenant les fichiers à restaurer (racine de la paire).</param>
    /// <param name="DestinationFolder">Dossier de destination sur la machine cible.</param>
    /// <param name="IsEncrypted">Vrai si les fichiers source sont chiffrés AES-256 (format WinBack).</param>
    /// <param name="DecryptionKey">Clé AES-256 (32 octets). Obligatoire si <paramref name="IsEncrypted"/> est vrai.</param>
    /// <param name="Overwrite">Si faux, les fichiers déjà présents à la destination sont ignorés.</param>
    public record RestoreOptions(
        string SourceFolder,
        string DestinationFolder,
        bool IsEncrypted,
        byte[]? DecryptionKey = null,
        bool Overwrite = true);

    /// <summary>Progression d'une restauration en cours.</summary>
    public record RestoreProgress(string CurrentFile, int FilesProcessed, int TotalFiles);

    /// <summary>Résultat d'une opération de restauration.</summary>
    public record RestoreResult(int Total, int Restored, int Skipped, int Errored, List<string> Errors);

    // ── Méthode principale ───────────────────────────────────────────────────

    /// <summary>
    /// Restaure tous les fichiers d'un dossier source vers la destination.
    /// La structure des sous-dossiers est préservée ;
    /// le chemin parent de la source n'est pas inclus dans la destination.
    /// </summary>
    public async Task<RestoreResult> RestoreAsync(
        RestoreOptions options,
        IProgress<RestoreProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (options.IsEncrypted && options.DecryptionKey == null)
            throw new ArgumentException(
                "Une clé de déchiffrement est requise pour les fichiers chiffrés.",
                nameof(options));

        if (!Directory.Exists(options.SourceFolder))
            throw new DirectoryNotFoundException(
                $"Dossier source introuvable : {options.SourceFolder}");

        // Énumérer récursivement tous les fichiers à restaurer.
        // On exclut le sous-dossier de corbeille interne (.winback_recycle).
        var files = Directory
            .EnumerateFiles(options.SourceFolder, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + ".winback_recycle" + Path.DirectorySeparatorChar))
            .ToList();

        int total = files.Count;
        int restored = 0, skipped = 0, errored = 0;
        var errors = new List<string>();

        _logger.LogInformation(
            "Début restauration : {Source} → {Dest} ({Total} fichier(s), chiffré={Encrypted})",
            options.SourceFolder, options.DestinationFolder, total, options.IsEncrypted);

        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var sourceFile = files[i];
            // Chemin relatif par rapport à la racine source (préserve la structure des sous-dossiers)
            var relativePath = Path.GetRelativePath(options.SourceFolder, sourceFile);
            var destFile = Path.Combine(options.DestinationFolder, relativePath);

            progress?.Report(new RestoreProgress(relativePath, i + 1, total));

            try
            {
                // Si Overwrite=false et que le fichier existe déjà, on le saute
                if (!options.Overwrite && File.Exists(destFile))
                {
                    skipped++;
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

                if (options.IsEncrypted)
                    await DecryptAndCopyAsync(sourceFile, destFile, options.DecryptionKey!, ct);
                else
                    await CopyFileAsync(sourceFile, destFile, ct);

                restored++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur restauration {File}", relativePath);
                errored++;
                errors.Add($"{relativePath}: {ex.Message}");
            }
        }

        _logger.LogInformation(
            "Restauration terminée : {Restored} restaurés, {Skipped} ignorés, {Errored} erreurs",
            restored, skipped, errored);

        return new RestoreResult(total, restored, skipped, errored, errors);
    }

    // ── Méthode publique partagée ────────────────────────────────────────────

    /// <summary>
    /// Dérive une clé AES-256 (32 octets) à partir d'un mot de passe.
    /// Algorithme : SHA-256(UTF-8(password)).
    /// <br/>
    /// Cette méthode est publique afin que la couche App puisse calculer
    /// la clé depuis la saisie utilisateur et la transmettre via
    /// <see cref="BackupEngineOptions.EncryptionKey"/>.
    /// Le même algorithme est utilisé à la sauvegarde et à la restauration,
    /// indépendamment de la machine.
    /// </summary>
    public static byte[] DeriveKey(string password)
        => SHA256.HashData(Encoding.UTF8.GetBytes(password));

    // ── Méthodes privées ─────────────────────────────────────────────────────

    /// <summary>
    /// Lit un fichier chiffré au format WinBack (16 octets IV en clair + données AES-256-CBC)
    /// et écrit le contenu déchiffré à la destination.
    /// </summary>
    private static async Task DecryptAndCopyAsync(
        string encryptedSource, string destination, byte[] key, CancellationToken ct)
    {
        await using var src = new FileStream(
            encryptedSource, FileMode.Open, FileAccess.Read,
            FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

        // Les 16 premiers octets sont l'IV (vecteur d'initialisation) stocké en clair.
        // Cet IV a été généré aléatoirement lors du chiffrement par BackupEngine.
        var iv = new byte[16];
        var read = await src.ReadAsync(iv, ct);
        if (read != 16)
            throw new InvalidDataException(
                $"Fichier chiffré invalide (IV incomplet) : {Path.GetFileName(encryptedSource)}");

        await using var dst = new FileStream(
            destination, FileMode.Create, FileAccess.Write,
            FileShare.None, 1024 * 1024, FileOptions.Asynchronous);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;

        // Lire les données chiffrées depuis le stream source (après l'IV) et déchiffrer à la volée
        await using var cs = new CryptoStream(src, aes.CreateDecryptor(), CryptoStreamMode.Read);
        await cs.CopyToAsync(dst, ct);
    }

    /// <summary>Copie un fichier sans chiffrement, en préservant la date de modification.</summary>
    private static async Task CopyFileAsync(string source, string dest, CancellationToken ct)
    {
        const int bufferSize = 1024 * 1024;
        await using var src = new FileStream(source, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize, FileOptions.Asynchronous);
        await src.CopyToAsync(dst, bufferSize, ct);
        File.SetLastWriteTimeUtc(dest, File.GetLastWriteTimeUtc(source));
    }
}
