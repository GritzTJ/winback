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
}
