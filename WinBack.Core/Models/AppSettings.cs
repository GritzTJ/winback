namespace WinBack.Core.Models;

/// <summary>
/// Paramètres globaux de l'application, stockés en base (singleton, Id = 1).
/// </summary>
public class AppSettings
{
    public int Id { get; set; } = 1;

    /// <summary>Démarrer WinBack automatiquement avec Windows.</summary>
    public bool StartWithWindows { get; set; } = true;

    /// <summary>Afficher les notifications Windows après chaque sauvegarde.</summary>
    public bool ShowNotifications { get; set; } = true;

    /// <summary>Mode avancé : expose les UUID, les options techniques.</summary>
    public bool AdvancedMode { get; set; } = false;

    /// <summary>Minimiser dans la barre système au démarrage (ne pas afficher la fenêtre principale).</summary>
    public bool StartMinimized { get; set; } = true;

    /// <summary>Niveau de log : 0=None, 1=Error, 2=Warning, 3=Info, 4=Debug.</summary>
    public int LogLevel { get; set; } = 3;

    /// <summary>Répertoire des fichiers de log. Null = répertoire AppData par défaut.</summary>
    public string? LogDirectory { get; set; }

    /// <summary>Langue de l'interface : "fr", "en". Null = langue système.</summary>
    public string? Language { get; set; }

    /// <summary>Nombre de tentatives en cas d'erreur de copie (0 = pas de retry).</summary>
    public int MaxRetryCount { get; set; } = 0;

    /// <summary>Délai en ms entre deux tentatives de copie.</summary>
    public int RetryDelayMs { get; set; } = 500;

    /// <summary>Cliquer sur une notification ouvre la fenêtre d'historique.</summary>
    public bool ClickableNotifications { get; set; } = true;

    /// <summary>Patterns d'exclusion globaux (JSON array), appliqués à toutes les paires de tous les profils.</summary>
    public string GlobalExcludePatternsJson { get; set; } = "[]";

    // Propriété calculée, non mappée en base
    private const int MaxPatternCount = 500;

    [System.Text.Json.Serialization.JsonIgnore]
    public List<string> GlobalExcludePatterns
    {
        get
        {
            try
            {
                var patterns = System.Text.Json.JsonSerializer.Deserialize<List<string>>(GlobalExcludePatternsJson) ?? [];
                return patterns.Count > MaxPatternCount ? patterns.GetRange(0, MaxPatternCount) : patterns;
            }
            catch (System.Text.Json.JsonException)
            {
                return [];
            }
        }
        set => GlobalExcludePatternsJson = System.Text.Json.JsonSerializer.Serialize(value);
    }
}
