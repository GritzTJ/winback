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
        ScanDirectory(sourcePath, sourcePath, pair, snapshotIndex, foundPaths, added, modified, progress);

        // Fichiers présents dans le snapshot mais absents du scan → Supprimés
        foreach (var snap in existingSnapshots)
        {
            if (!foundPaths.Contains(snap.RelativePath))
                deleted.Add(snap.RelativePath);
        }

        return new DiffResult(added, modified, deleted);
    }

    private static void ScanDirectory(
        string rootPath,
        string currentPath,
        BackupPair pair,
        Dictionary<string, FileSnapshot> snapshotIndex,
        HashSet<string> foundPaths,
        List<string> added,
        List<string> modified,
        IProgress<string>? progress)
    {
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(currentPath);
        }
        catch (UnauthorizedAccessException) { return; }
        catch (IOException) { return; }

        foreach (var entry in entries)
        {
            var relativePath = Path.GetRelativePath(rootPath, entry);

            if (pair.IsExcluded(relativePath))
                continue;

            if (Directory.Exists(entry))
            {
                ScanDirectory(rootPath, entry, pair, snapshotIndex, foundPaths, added, modified, progress);
            }
            else
            {
                try
                {
                    var info = new FileInfo(entry);
                    foundPaths.Add(relativePath);

                    if (snapshotIndex.TryGetValue(relativePath, out var snap))
                    {
                        // Comparer taille ET date de modification (précision à la seconde)
                        var lastModified = info.LastWriteTimeUtc;
                        if (info.Length != snap.Size ||
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
                catch (IOException) { /* Fichier inaccessible, on l'ignore */ }
            }
        }
    }

    /// <summary>
    /// Calcule le hash MD5 d'un fichier pour la vérification d'intégrité.
    /// </summary>
    public static async Task<string> ComputeHashAsync(string filePath, CancellationToken ct = default)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await md5.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash);
    }
}

public record DiffResult(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Modified,
    IReadOnlyList<string> Deleted)
{
    public int TotalChanges => Added.Count + Modified.Count + Deleted.Count;
    public bool HasChanges => TotalChanges > 0;
}
