using Microsoft.Win32;
using System.Windows;
using WinBack.App.ViewModels;

namespace WinBack.App.Views;

/// <summary>
/// Fenêtre de restauration d'une sauvegarde WinBack.
/// Permet de restaurer des fichiers en clair ou chiffrés AES-256
/// vers un dossier de destination choisi par l'utilisateur.
/// </summary>
public partial class RestoreWindow : Window
{
    private readonly RestoreViewModel _vm;

    public RestoreWindow(RestoreViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    // ── Sélection des dossiers ────────────────────────────────────────────────

    private void BrowseSource_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choisir le dossier de sauvegarde à restaurer"
        };
        if (dialog.ShowDialog() == true)
            _vm.SourceFolder = dialog.FolderName;
    }

    private void BrowseDest_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choisir le dossier de destination"
        };
        if (dialog.ShowDialog() == true)
            _vm.DestinationFolder = dialog.FolderName;
    }

    // ── Mot de passe ─────────────────────────────────────────────────────────

    private void PasswordBox_Changed(object sender, RoutedEventArgs e)
    {
        // Le PasswordBox ne supporte pas le binding direct : on passe par le code-behind.
        _vm.Password = ((System.Windows.Controls.PasswordBox)sender).Password;
    }
}
