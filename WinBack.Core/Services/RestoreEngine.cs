using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace WinBack.Core.Services;

/// <summary>
/// Moteur de restauration : copie les fichiers d'un dossier de sauvegarde vers
/// une destination choisie par l'utilisateur.
/// Supporte les fichiers en clair et les fichiers chiffrés AES-256-CBC
/// (format WinBack v2 : magic "WB02" + IV + ciphertext + HMAC-SHA256 ;
/// rétrocompatible avec le format v1 legacy : IV + ciphertext sans authentification).
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
    /// <param name="IncludedPaths">
    /// Ensemble des chemins relatifs à restaurer (restauration sélective).
    /// Si <c>null</c> ou vide, tous les fichiers sont restaurés.
    /// Les chemins sont comparés sans tenir compte de la casse.
    /// </param>
    public record RestoreOptions(
        string SourceFolder,
        string DestinationFolder,
        bool IsEncrypted,
        byte[]? DecryptionKey = null,
        bool Overwrite = true,
        IReadOnlySet<string>? IncludedPaths = null);

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

        // Restauration sélective : filtrer selon les chemins sélectionnés par l'utilisateur.
        // Le HashSet est créé avec StringComparer.OrdinalIgnoreCase (insensible à la casse)
        // pour assurer la correspondance sur tous les systèmes de fichiers Windows.
        if (options.IncludedPaths is { Count: > 0 } included)
        {
            files = files
                .Where(f => included.Contains(Path.GetRelativePath(options.SourceFolder, f)))
                .ToList();
        }

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
            var destFile = Path.GetFullPath(Path.Combine(options.DestinationFolder, relativePath));

            // Protection contre le path traversal : vérifier que le fichier de destination
            // reste bien à l'intérieur du dossier de destination choisi par l'utilisateur.
            var destRoot = Path.GetFullPath(options.DestinationFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!destFile.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Path traversal détecté, fichier ignoré : {RelativePath}", relativePath);
                errored++;
                errors.Add($"{relativePath}: chemin relatif invalide (path traversal)");
                continue;
            }

            progress?.Report(new RestoreProgress(relativePath, i + 1, total));

            try
            {
                // Si Overwrite=false et que le fichier existe déjà, on le saute
                if (!options.Overwrite && File.Exists(destFile))
                {
                    skipped++;
                    continue;
                }

                var destDir = Path.GetDirectoryName(destFile);
                if (destDir != null) Directory.CreateDirectory(destDir);

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
    /// Dérive une clé AES-256 (32 octets) à partir d'un mot de passe (KDF legacy).
    /// Algorithme : SHA-256(UTF-8(password)).
    /// <br/>
    /// Conservée pour la rétrocompatibilité avec les sauvegardes créées avant la v0.4.4.
    /// Pour les nouvelles sauvegardes, utiliser <see cref="DeriveKeyV2"/>.
    /// </summary>
    public static byte[] DeriveKey(string password)
        => SHA256.HashData(Encoding.UTF8.GetBytes(password));

    /// <summary>
    /// Nombre d'itérations PBKDF2 pour la dérivation de clé v2 (conforme OWASP 2023).
    /// </summary>
    public const int Pbkdf2Iterations = 600_000;

    /// <summary>
    /// Dérive une clé AES-256 (32 octets) à partir d'un mot de passe et d'un sel.
    /// Algorithme : PBKDF2-SHA256 avec 600 000 itérations.
    /// </summary>
    public static byte[] DeriveKeyV2(string password, byte[] salt)
    {
        using var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(
            password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(32);
    }

    /// <summary>Génère un sel cryptographique de 32 octets.</summary>
    public static byte[] GenerateSalt()
        => System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    /// <summary>
    /// Métadonnées KDF écrites dans chaque dossier de destination chiffrée
    /// pour permettre la restauration cross-machine.
    /// </summary>
    public record KdfMetadata(int KdfVersion, string Salt, int Iterations);

    /// <summary>Nom du fichier de métadonnées KDF écrit dans les dossiers de destination chiffrés.</summary>
    public const string KdfMetadataFileName = ".winback_kdf.json";

    // ── Méthodes privées ─────────────────────────────────────────────────────

    /// <summary>
    /// Lit un fichier chiffré au format WinBack et écrit le contenu déchiffré à la destination.
    /// Supporte deux formats :
    /// <list type="bullet">
    /// <item><b>v2</b> : "WB02" (4) + IV (16) + ciphertext AES-256-CBC/PKCS7 + HMAC-SHA256 (32)</item>
    /// <item><b>v1 (legacy)</b> : IV (16) + ciphertext AES-256-CBC (sans authentification)</item>
    /// </list>
    /// </summary>
    private async Task DecryptAndCopyAsync(
        string encryptedSource, string destination, byte[] key, CancellationToken ct)
    {
        await using var src = new FileStream(
            encryptedSource, FileMode.Open, FileAccess.Read,
            FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

        // Lire les 4 premiers octets pour détecter la version du format
        var magic = new byte[4];
        if (await src.ReadAsync(magic, ct) != 4)
            throw new InvalidDataException(
                $"Fichier chiffré invalide (trop court) : {Path.GetFileName(encryptedSource)}");

        bool isV2 = magic[0] == (byte)'W' && magic[1] == (byte)'B'
                  && magic[2] == (byte)'0' && magic[3] == (byte)'2';

        byte[] iv;

        if (isV2)
        {
            // ── Format v2 : vérification HMAC puis déchiffrement ──────────────
            iv = new byte[16];
            if (await src.ReadAsync(iv, ct) != 16)
                throw new InvalidDataException(
                    $"Fichier chiffré invalide (IV incomplet) : {Path.GetFileName(encryptedSource)}");

            // HMAC est sur les 32 derniers octets du fichier
            var fileLen = src.Length;
            var hmacDataLen = fileLen - 4 - 32; // IV + ciphertext (tout entre magic et HMAC)
            if (hmacDataLen < 16) // au minimum 16 octets d'IV
                throw new InvalidDataException(
                    $"Fichier chiffré invalide (taille incohérente) : {Path.GetFileName(encryptedSource)}");

            // Vérifier le HMAC : lire (IV + ciphertext) et comparer avec le HMAC stocké
            var hmacKey = SHA256.HashData(key);
            src.Position = 4; // après magic
            using var hmac = System.Security.Cryptography.IncrementalHash.CreateHMAC(
                System.Security.Cryptography.HashAlgorithmName.SHA256, hmacKey);
            var remaining = hmacDataLen;
            var buffer = new byte[65536];
            while (remaining > 0)
            {
                var toRead = (int)Math.Min(remaining, buffer.Length);
                var read = await src.ReadAsync(buffer.AsMemory(0, toRead), ct);
                if (read == 0) break;
                hmac.AppendData(buffer, 0, read);
                remaining -= read;
            }
            var computed = hmac.GetHashAndReset();

            var stored = new byte[32];
            if (await src.ReadAsync(stored, ct) != 32)
                throw new InvalidDataException(
                    $"Fichier chiffré invalide (HMAC manquant) : {Path.GetFileName(encryptedSource)}");

            if (!CryptographicOperations.FixedTimeEquals(computed, stored))
                throw new InvalidDataException(
                    $"Intégrité du fichier chiffré compromise (HMAC invalide) : {Path.GetFileName(encryptedSource)}. " +
                    "Le fichier a peut-être été altéré.");

            // Déchiffrer : positionner après magic + IV, limiter la lecture au ciphertext
            src.Position = 20; // 4 magic + 16 IV
            var ciphertextLen = fileLen - 4 - 16 - 32;

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            await using var dst = new FileStream(
                destination, FileMode.Create, FileAccess.Write,
                FileShare.None, 1024 * 1024, FileOptions.Asynchronous);

            await using var limited = new LengthLimitedStream(src, ciphertextLen);
            await using var cs = new CryptoStream(limited, aes.CreateDecryptor(), CryptoStreamMode.Read);
            await cs.CopyToAsync(dst, ct);
        }
        else
        {
            // ── Format v1 (legacy) : pas de HMAC, pas de magic ────────────────
            _logger.LogWarning(
                "Format de chiffrement v1 (sans authentification) détecté : {File}. " +
                "Relancez une sauvegarde pour migrer automatiquement vers le format v2.",
                Path.GetFileName(encryptedSource));

            // Les 4 octets lus comme magic sont en réalité le début de l'IV
            iv = new byte[16];
            Array.Copy(magic, iv, 4);
            if (await src.ReadAsync(iv.AsMemory(4), ct) != 12)
                throw new InvalidDataException(
                    $"Fichier chiffré invalide (IV incomplet) : {Path.GetFileName(encryptedSource)}");

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            await using var dst = new FileStream(
                destination, FileMode.Create, FileAccess.Write,
                FileShare.None, 1024 * 1024, FileOptions.Asynchronous);

            await using var cs = new CryptoStream(src, aes.CreateDecryptor(), CryptoStreamMode.Read);
            await cs.CopyToAsync(dst, ct);
        }
    }

    /// <summary>
    /// Stream en lecture seule qui limite le nombre d'octets lus depuis un stream sous-jacent.
    /// Utilisé pour isoler le ciphertext du HMAC lors du déchiffrement v2.
    /// </summary>
    private sealed class LengthLimitedStream(Stream inner, long length) : Stream
    {
        private long _remaining = length;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0) return 0;
            var read = inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
            _remaining -= read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_remaining <= 0) return 0;
            var toRead = (int)Math.Min(buffer.Length, _remaining);
            var read = await inner.ReadAsync(buffer[..toRead], ct);
            _remaining -= read;
            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => length - _remaining; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
