using System.Text.Json;

namespace WinBack.Core.Models;

public class BackupPair
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public BackupProfile Profile { get; set; } = null!;

    /// <summary>Chemin absolu du dossier source, ex: C:\Users\Papa\Documents</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Chemin relatif à la racine du disque de destination, ex: Documents</summary>
    public string DestRelativePath { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>Patterns d'exclusion sérialisés en JSON, ex: ["*.tmp","~$*",".git"]</summary>
    public string ExcludePatternsJson { get; set; } = "[]";

    public List<FileSnapshot> Snapshots { get; set; } = [];

    // Propriété calculée, non mappée en base
    public List<string> ExcludePatterns
    {
        get => JsonSerializer.Deserialize<List<string>>(ExcludePatternsJson) ?? [];
        set => ExcludePatternsJson = JsonSerializer.Serialize(value);
    }

    /// <summary>Vérifie si un chemin relatif correspond à un pattern d'exclusion.</summary>
    public bool IsExcluded(string relativePath)
    {
        var patterns = ExcludePatterns;
        if (patterns.Count == 0) return false;

        var fileName = Path.GetFileName(relativePath);
        foreach (var pattern in patterns)
        {
            if (MatchesGlob(fileName, pattern) || MatchesGlob(relativePath, pattern))
                return true;
        }
        return false;
    }

    private static bool MatchesGlob(string text, string pattern)
    {
        // Conversion simple glob → regex via itération
        int pi = 0, ti = 0, starPi = -1, starTi = -1;
        var p = pattern.AsSpan();
        var t = text.AsSpan();

        while (ti < t.Length)
        {
            if (pi < p.Length && (p[pi] == '?' || char.ToLowerInvariant(p[pi]) == char.ToLowerInvariant(t[ti])))
            {
                pi++; ti++;
            }
            else if (pi < p.Length && p[pi] == '*')
            {
                starPi = pi++; starTi = ti;
            }
            else if (starPi != -1)
            {
                pi = starPi + 1; ti = ++starTi;
            }
            else return false;
        }

        while (pi < p.Length && p[pi] == '*') pi++;
        return pi == p.Length;
    }
}
