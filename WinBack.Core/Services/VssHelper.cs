using System.Management;
using System.Runtime.Versioning;

namespace WinBack.Core.Services;

/// <summary>
/// Gère les snapshots VSS (Volume Shadow Copy Service) pour permettre la copie
/// de fichiers ouverts (ex: .pst Outlook, bases de données).
/// Nécessite des droits administrateur.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class VssSnapshot : IDisposable
{
    private readonly string _shadowId;
    private readonly string _deviceObject;
    private bool _disposed;

    private VssSnapshot(string shadowId, string deviceObject)
    {
        _shadowId = shadowId;
        _deviceObject = deviceObject;
    }

    /// <summary>
    /// Chemin racine du snapshot VSS, ex: \\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1\
    /// Remplacer la lettre de lecteur source par ce chemin pour lire les fichiers ouverts.
    /// </summary>
    public string DevicePath => _deviceObject.TrimEnd('\\') + '\\';

    /// <summary>
    /// Traduit un chemin absolu source en chemin VSS équivalent.
    /// Ex: "C:\Users\Papa\doc.pst" → "\\?\GLOBALROOT\...\Users\Papa\doc.pst"
    /// </summary>
    public string TranslatePath(string absoluteSourcePath, string volumeRoot)
    {
        var relative = Path.GetRelativePath(volumeRoot, absoluteSourcePath);
        return Path.Combine(DevicePath, relative);
    }

    /// <summary>
    /// Crée un snapshot VSS du volume spécifié (ex: "C:\").
    /// Retourne null si la création échoue (pas de droits, VSS désactivé, etc.).
    /// </summary>
    public static VssSnapshot? Create(string volumePath)
    {
        try
        {
            using var shadowClass = new ManagementClass("Win32_ShadowCopy");
            using var inParams = shadowClass.GetMethodParameters("Create");
            inParams["Volume"] = volumePath.TrimEnd('\\') + '\\';
            inParams["Context"] = "ClientAccessible";

            using var outParams = shadowClass.InvokeMethod("Create", inParams, null);
            if (outParams == null) return null;

            int returnValue = Convert.ToInt32(outParams["ReturnValue"]);
            if (returnValue != 0) return null;

            string shadowId = outParams["ShadowID"]?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(shadowId)) return null;

            // Récupérer le DeviceObject du snapshot créé
            using var shadow = new ManagementObject($"Win32_ShadowCopy.ID='{shadowId}'");
            shadow.Get();
            var deviceObject = shadow["DeviceObject"]?.ToString() ?? string.Empty;

            return new VssSnapshot(shadowId, deviceObject);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Vérifie si VSS est disponible et fonctionnel sur le système.
    /// </summary>
    public static bool IsAvailable()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ShadowCopy");
            searcher.Get();
            return true;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DeleteShadow(_shadowId);
    }

    private static void DeleteShadow(string shadowId)
    {
        if (string.IsNullOrEmpty(shadowId)) return;
        try
        {
            using var shadow = new ManagementObject($"Win32_ShadowCopy.ID='{shadowId}'");
            shadow.InvokeMethod("Delete", null, null);
        }
        catch { /* Ignorer les erreurs de suppression */ }
    }
}

/// <summary>
/// Gestionnaire de snapshots VSS par volume, avec cache pour éviter
/// de créer plusieurs snapshots du même volume.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class VssSessionManager : IDisposable
{
    private readonly Dictionary<string, VssSnapshot?> _snapshots = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>
    /// Obtient (ou crée) un snapshot VSS pour le volume racine d'un fichier.
    /// Retourne null si VSS échoue ou est désactivé.
    /// </summary>
    public VssSnapshot? GetOrCreate(string volumeRoot)
    {
        var key = volumeRoot.TrimEnd('\\').ToUpperInvariant();
        if (_snapshots.TryGetValue(key, out var existing))
            return existing;

        var snapshot = VssSnapshot.Create(volumeRoot);
        _snapshots[key] = snapshot;
        return snapshot;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var snap in _snapshots.Values)
            snap?.Dispose();
        _snapshots.Clear();
    }
}
