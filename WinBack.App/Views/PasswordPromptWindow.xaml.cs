using System.Windows;
using System.Windows.Input;
using WinBack.Core.Services;

namespace WinBack.App.Views;

/// <summary>
/// Fenêtre modale demandant le mot de passe de chiffrement avant une sauvegarde chiffrée.
/// Retourne la clé AES-256 dérivée via <see cref="RestoreEngine.DeriveKeyV2"/> (PBKDF2)
/// si un sel est fourni, ou <see cref="RestoreEngine.DeriveKey"/> (legacy SHA-256) sinon.
/// </summary>
public partial class PasswordPromptWindow : Window
{
    private byte[]? _salt;

    /// <summary>Clé AES-256 dérivée du mot de passe saisi. Null si annulé.</summary>
    public byte[]? DerivedKey { get; private set; }

    public PasswordPromptWindow()
    {
        InitializeComponent();
    }

    /// <summary>Initialise la fenêtre avec le nom du profil et le sel PBKDF2 optionnel.</summary>
    public void InitForProfile(string profileName, byte[]? salt = null)
    {
        ProfileNameBlock.Text = profileName;
        _salt = salt;
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
        // Dériver la clé — PBKDF2 si un sel est disponible, sinon legacy SHA-256
        DerivedKey = _salt != null
            ? RestoreEngine.DeriveKeyV2(password, _salt)
            : RestoreEngine.DeriveKey(password);
        PasswordBox.Clear();
        DialogResult = true;
        Close();
    }
}
