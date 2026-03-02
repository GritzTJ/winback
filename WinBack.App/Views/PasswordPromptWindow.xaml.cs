using System.Windows;
using System.Windows.Input;
using WinBack.Core.Services;

namespace WinBack.App.Views;

/// <summary>
/// Fenêtre modale demandant le mot de passe de chiffrement avant une sauvegarde chiffrée.
/// Retourne la clé AES-256 dérivée via <see cref="RestoreEngine.DeriveKey"/> si l'utilisateur
/// confirme, ou null si l'utilisateur annule.
/// </summary>
public partial class PasswordPromptWindow : Window
{
    /// <summary>Clé AES-256 dérivée du mot de passe saisi. Null si annulé.</summary>
    public byte[]? DerivedKey { get; private set; }

    public PasswordPromptWindow()
    {
        InitializeComponent();
    }

    /// <summary>Initialise la fenêtre avec le nom du profil à afficher.</summary>
    public void InitForProfile(string profileName)
    {
        ProfileNameBlock.Text = profileName;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => TryConfirm();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DerivedKey = null;
        DialogResult = false;
        Close();
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        // Valider avec Entrée pour un flux rapide
        if (e.Key == Key.Enter) TryConfirm();
    }

    private void TryConfirm()
    {
        var password = PasswordBox.Password;
        if (string.IsNullOrEmpty(password))
        {
            ErrorBlock.Visibility = Visibility.Visible;
            PasswordBox.Focus();
            return;
        }

        ErrorBlock.Visibility = Visibility.Collapsed;
        // Dériver la clé à partir du mot de passe — même algorithme que la sauvegarde
        DerivedKey = RestoreEngine.DeriveKey(password);
        DialogResult = true;
        Close();
    }
}
