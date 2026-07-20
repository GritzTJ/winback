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

    private const int MaxPatternCount = 500;

    // Propriété calculée, non mappée en base
    public List<string> ExcludePatterns
    {
        get
        {
            try
            {
                var patterns = JsonSerializer.Deserialize<List<string>>(ExcludePatternsJson) ?? [];
                return patterns.Count > MaxPatternCount ? patterns.GetRange(0, MaxPatternCount) : patterns;
            }
            catch (JsonException)
            {
                return [];
            }
        }
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

    /// <summary>Vérifie si un chemin relatif est exclu par une liste de patterns globaux.</summary>
    public static bool IsExcludedByPatterns(string relativePath, IReadOnlyList<string> patterns)
    {
        if (patterns.Count == 0) return false;
        var fileName = Path.GetFileName(relativePath);
        foreach (var pattern in patterns)
        {
            if (MatchesGlob(fileName, pattern) || MatchesGlob(relativePath, pattern))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Normalise un caractère pour la comparaison de glob : séparateurs de chemin
    /// unifiés et casse ignorée.
    /// <para>
    /// Les patterns sont saisis indifféremment avec <c>/</c> (style Unix, celui des
    /// .gitignore dont les utilisateurs s'inspirent) ou <c>\</c>, alors que les chemins
    /// relatifs calculés par <see cref="Path.GetRelativePath"/> utilisent <c>\</c> sous
    /// Windows. Sans cette unification, un pattern aussi courant que
    /// <c>node_modules/**</c> ne correspondrait jamais à <c>node_modules\lodash\index.js</c>
    /// et l'exclusion serait silencieusement sans effet.
    /// </para>
    /// </summary>
    private static char NormalizeGlobChar(char c)
        => c == '/' ? '\\' : char.ToLowerInvariant(c);

    internal static bool MatchesGlob(string text, string pattern)
    {
        // Conversion simple glob → regex via itération
        int pi = 0, ti = 0, starPi = -1, starTi = -1;
        var p = pattern.AsSpan();
        var t = text.AsSpan();

        while (ti < t.Length)
        {
            if (pi < p.Length && (p[pi] == '?' || NormalizeGlobChar(p[pi]) == NormalizeGlobChar(t[ti])))
            {
                pi++; ti++;
            }
            else if (pi < p.Length && p[pi] == '*')
            {
                // '*' couvre n'importe quelle séquence, séparateurs compris : '**' est
                // donc géré naturellement par le retour arrière ci-dessous.
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
