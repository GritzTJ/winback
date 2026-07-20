using System.Diagnostics;
using WinBack.Core.Models;

namespace WinBack.Core.Services;

/// <summary>
/// Calcule les différences entre l'état actuel du disque source et le dernier snapshot connu.
/// </summary>
public class DiffCalculator
{
    /// <summary>
    /// Parcourt récursivement sourcePath et compare avec les snapshots existants.
    /// Retourne les listes de fichiers Ajoutés, Modifiés et Supprimés.
    /// </summary>
    public DiffResult Compute(
        string sourcePath,
        IReadOnlyList<FileSnapshot> existingSnapshots,
        BackupPair pair,
        IReadOnlyList<string>? globalExcludePatterns = null,
        IProgress<string>? progress = null)
    {
        var added = new List<string>();
        var modified = new List<string>();
        var deleted = new List<string>();

        if (!Directory.Exists(sourcePath))
        {
            // Source disparue : tout est supprimé
            deleted.AddRange(existingSnapshots.Select(s => s.RelativePath));
            return new DiffResult(added, modified, deleted);
        }

        // Index des snapshots par chemin relatif (insensible à la casse Windows)
        var snapshotIndex = existingSnapshots.ToDictionary(
            s => s.RelativePath,
            s => s,
            StringComparer.OrdinalIgnoreCase);

        // Ensemble des chemins trouvés lors du scan (pour détecter les suppressions)
        var foundPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        progress?.Report($"Analyse de {sourcePath}…");
        bool scanComplete = ScanDirectory(
            sourcePath, sourcePath, pair, globalExcludePatterns, snapshotIndex, foundPaths, added, modified, progress);

        // Fichiers présents dans le snapshot mais absents du scan → Supprimés.
        //
        // SÉCURITÉ : si une partie de l'arborescence n'a pas pu être lue (permissions,
        // verrou antivirus, disque qui se déconnecte…), l'absence d'un fichier dans
        // foundPaths ne prouve PAS qu'il a été supprimé à la source. Propager des
        // suppressions dans ce cas effacerait des fichiers encore vivants du disque de
        // sauvegarde (stratégie Mirror). On abandonne donc entièrement la détection de
        // suppressions pour ce scan : les copies/mises à jour restent effectuées.
        if (scanComplete)
        {
            foreach (var snap in existingSnapshots)
            {
                if (foundPaths.Contains(snap.RelativePath)) continue;

                // Un fichier désormais couvert par un pattern d'exclusion n'a pas été
                // supprimé à la source : il est simplement sorti du périmètre. On le
                // laisse en place sur la destination plutôt que de l'effacer.
                if (pair.IsExcluded(snap.RelativePath) ||
                    (globalExcludePatterns?.Count > 0 &&
                     BackupPair.IsExcludedByPatterns(snap.RelativePath, globalExcludePatterns)))
                    continue;

                deleted.Add(snap.RelativePath);
            }
        }

        return new DiffResult(added, modified, deleted, ScanIncomplete: !scanComplete);
    }

    /// <summary>
    /// Parcourt récursivement un dossier.
    /// Retourne <c>false</c> si une partie de l'arborescence n'a pas pu être lue,
    /// auquel cas la liste des fichiers trouvés est incomplète.
    /// </summary>
    private static bool ScanDirectory(
        string rootPath,
        string currentPath,
        BackupPair pair,
        IReadOnlyList<string>? globalExcludePatterns,
        Dictionary<string, FileSnapshot> snapshotIndex,
        HashSet<string> foundPaths,
        List<string> added,
        List<string> modified,
        IProgress<string>? progress)
    {
        List<string> entries;
        try
        {
            // Matérialisé immédiatement : une IOException levée pendant l'itération
            // paresseuse laisserait un scan partiel passer pour un scan complet.
            entries = Directory.EnumerateFileSystemEntries(currentPath).ToList();
        }
        catch (UnauthorizedAccessException ex) // Dossier non accessible par l'utilisateur courant
        {
            Trace.WriteLine($"[DiffCalculator] Dossier illisible {currentPath} : {ex.Message}");
            return false;
        }
        catch (IOException ex) // Dossier disparu ou verrouillé pendant l'énumération
        {
            Trace.WriteLine($"[DiffCalculator] Dossier illisible {currentPath} : {ex.Message}");
            return false;
        }

        bool complete = true;

        foreach (var entry in entries)
        {
            var relativePath = Path.GetRelativePath(rootPath, entry);

            if (pair.IsExcluded(relativePath) ||
                (globalExcludePatterns?.Count > 0 && BackupPair.IsExcludedByPatterns(relativePath, globalExcludePatterns)))
                continue;

            if (Directory.Exists(entry))
            {
                // Ignorer les symlinks et jonctions pour éviter les boucles infinies et l'évasion de périmètre
                bool isReparsePoint;
                try
                {
                    isReparsePoint = new DirectoryInfo(entry).Attributes.HasFlag(FileAttributes.ReparsePoint);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Attributs illisibles : le contenu du dossier l'est probablement aussi.
                    Trace.WriteLine($"[DiffCalculator] Attributs illisibles {entry} : {ex.Message}");
                    complete = false;
                    continue;
                }

                if (isReparsePoint) continue;

                if (!ScanDirectory(rootPath, entry, pair, globalExcludePatterns, snapshotIndex, foundPaths, added, modified, progress))
                    complete = false;
            }
            else
            {
                try
                {
                    var info = new FileInfo(entry);
                    // Length lève si le fichier a disparu entre l'énumération et ici :
                    // on le lit avant d'enregistrer le chemin comme « trouvé ».
                    var length = info.Length;
                    var lastModified = info.LastWriteTimeUtc;
                    foundPaths.Add(relativePath);

                    if (snapshotIndex.TryGetValue(relativePath, out var snap))
                    {
                        // Comparer taille ET date de modification (précision à la seconde)
                        if (length != snap.Size ||
                            Math.Abs((lastModified - snap.LastModified).TotalSeconds) > 2)
                        {
                            modified.Add(relativePath);
                        }
                        // Sinon : inchangé, on ne fait rien
                    }
                    else
                    {
                        added.Add(relativePath);
                    }

                    progress?.Report(relativePath);
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    // Le fichier a réellement disparu entre l'énumération et la lecture :
                    // c'est une suppression légitime, le scan reste fiable.
                    Trace.WriteLine($"[DiffCalculator] Fichier disparu pendant le scan {entry} : {ex.Message}");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Fichier verrouillé ou refusé : on ne peut pas conclure qu'il a disparu.
                    Trace.WriteLine($"[DiffCalculator] Fichier illisible {entry} : {ex.Message}");
                    complete = false;
                }
            }
        }

        return complete;
    }

    /// <summary>
    /// Calcule le hash SHA-256 d'un fichier pour la vérification d'intégrité.
    /// </summary>
    public static async Task<string> ComputeHashAsync(string filePath, CancellationToken ct = default)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash);
    }
}

/// <param name="ScanIncomplete">
/// Vrai si une partie de l'arborescence source n'a pas pu être lue.
/// Dans ce cas <see cref="Deleted"/> est volontairement vide : on ne peut pas
/// distinguer un fichier supprimé d'un fichier temporairement illisible, et
/// propager la suppression détruirait des données sur le disque de sauvegarde.
/// </param>
public record DiffResult(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Modified,
    IReadOnlyList<string> Deleted,
    bool ScanIncomplete = false)
{
    public int TotalChanges => Added.Count + Modified.Count + Deleted.Count;
    public bool HasChanges => TotalChanges > 0;
}
