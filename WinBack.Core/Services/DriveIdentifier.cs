using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace WinBack.Core.Services;

/// <summary>
/// Identifie les disques par leur GUID de volume Windows (stable entre les reconnexions).
/// L'UUID est de la forme {xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}.
/// </summary>
public static class DriveIdentifier
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetVolumeNameForVolumeMountPoint(
        string lpszVolumeMountPoint,
        StringBuilder lpszVolumeName,
        uint cchBufferLength);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetVolumeInformation(
        string lpRootPathName,
        StringBuilder? lpVolumeNameBuffer,
        uint nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        StringBuilder? lpFileSystemNameBuffer,
        uint nFileSystemNameSize);

    /// <summary>
    /// Retourne le GUID de volume pour une lettre de lecteur (ex: "E:\").
    /// Retourne null en cas d'échec.
    /// </summary>
    public static string? GetVolumeGuid(string driveLetter)
    {
        // Normaliser en "E:\"
        var mountPoint = driveLetter.TrimEnd('\\') + '\\';
        var sb = new StringBuilder(50);

        if (!GetVolumeNameForVolumeMountPoint(mountPoint, sb, (uint)sb.Capacity))
            return null;

        // Format retourné : \\?\Volume{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}\
        // On extrait juste le GUID entre accolades
        var raw = sb.ToString();
        var start = raw.IndexOf('{');
        var end = raw.IndexOf('}');
        if (start >= 0 && end > start)
            return raw.Substring(start, end - start + 1);

        return null;
    }

    /// <summary>
    /// Retourne l'étiquette du volume pour une lettre de lecteur.
    /// </summary>
    public static string? GetVolumeLabel(string driveLetter)
    {
        var mountPoint = driveLetter.TrimEnd('\\') + '\\';
        var label = new StringBuilder(256);
        if (GetVolumeInformation(mountPoint, label, (uint)label.Capacity,
                out _, out _, out _, null, 0))
            return label.ToString();
        return null;
    }

    /// <summary>
    /// Retourne le numéro de série du disque physique via WMI.
    /// </summary>
    public static string? GetDiskSerialNumber(string driveLetter)
    {
        try
        {
            var letter = driveLetter.TrimEnd('\\', ':');
            using var searcher = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_LogicalDiskToPartition");

            // Requête en deux étapes : LogicalDisk → Partition → DiskDrive
            using var ldSearcher = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_LogicalDisk WHERE DeviceID='{letter}:'");

            foreach (ManagementObject ld in ldSearcher.Get())
            {
                foreach (ManagementObject part in ld.GetRelated("Win32_DiskPartition"))
                {
                    foreach (ManagementObject disk in part.GetRelated("Win32_DiskDrive"))
                    {
                        return disk["SerialNumber"]?.ToString()?.Trim();
                    }
                }
            }
        }
        catch { /* WMI peut échouer si non admin */ }
        return null;
    }

    /// <summary>
    /// Retourne la lettre de lecteur montée pour un GUID de volume donné.
    /// Retourne null si le volume n'est pas actuellement monté.
    /// </summary>
    public static string? FindDriveLetterByGuid(string volumeGuid)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is not (DriveType.Removable or DriveType.Fixed))
                continue;
            try
            {
                var guid = GetVolumeGuid(drive.Name);
                if (guid != null && string.Equals(guid, volumeGuid, StringComparison.OrdinalIgnoreCase))
                    return drive.Name;
            }
            catch { /* drive peut disparaître pendant l'itération */ }
        }
        return null;
    }

    /// <summary>
    /// Retourne toutes les informations d'un lecteur externe nouvellement inséré.
    /// </summary>
    public static DriveDetails? GetDriveDetails(string driveLetter)
    {
        try
        {
            var guid = GetVolumeGuid(driveLetter);
            if (guid == null) return null;

            return new DriveDetails(
                DriveLetter: driveLetter.TrimEnd('\\'),
                VolumeGuid: guid,
                Label: GetVolumeLabel(driveLetter) ?? driveLetter,
                SerialNumber: GetDiskSerialNumber(driveLetter));
        }
        catch { return null; }
    }
}

public record DriveDetails(
    string DriveLetter,
    string VolumeGuid,
    string Label,
    string? SerialNumber);
