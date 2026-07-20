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
    private bool _requireConfirmation;

    /// <summary>Clé AES-256 dérivée du mot de passe saisi. Null si annulé.</summary>
    public byte[]? DerivedKey { get; private set; }

    public PasswordPromptWindow()
    {
        InitializeComponent();
    }

    /// <summary>Initialise la fenêtre avec le nom du profil et le sel PBKDF2 optionnel.</summary>
    /// <param name="profileName">Nom du profil affiché à l'utilisateur.</param>
    /// <param name="salt">Sel PBKDF2 ; <c>null</c> = KDF legacy SHA-256.</param>
    /// <param name="requireConfirmation">
    /// Vrai lors de la première sauvegarde chiffrée du profil. Aucun mot de passe de
    /// référence n'existe alors : une faute de frappe chiffrerait toute la sauvegarde
    /// avec une clé que l'utilisateur ne pourra jamais reproduire. La double saisie est
    /// inutile ensuite, puisqu'un mot de passe erroné se détecte à la restauration.
    /// </param>
    public void InitForProfile(string profileName, byte[]? salt = null, bool requireConfirmation = false)
    {
        ProfileNameBlock.Text = profileName;
        _salt = salt;
        _requireConfirmation = requireConfirmation;

        if (requireConfirmation)
        {
            ExplanationBlock.Text =
                "Première sauvegarde chiffrée de ce profil. Choisissez un mot de passe et " +
                "conservez-le : sans lui, les fichiers sauvegardés seront définitivement illisibles.";
            ConfirmLabel.Visibility = Visibility.Visible;
            ConfirmPasswordBox.Visibility = Visibility.Visible;
        }
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
            ShowError("Le mot de passe ne peut pas être vide.");
            PasswordBox.Focus();
            return;
        }

        if (_requireConfirmation && !string.Equals(password, ConfirmPasswordBox.Password, StringComparison.Ordinal))
        {
            ShowError("Les deux mots de passe ne correspondent pas.");
            ConfirmPasswordBox.Clear();
            ConfirmPasswordBox.Focus();
            return;
        }

        ErrorBlock.Visibility = Visibility.Collapsed;
        // Dériver la clé — PBKDF2 si un sel est disponible, sinon legacy SHA-256
        DerivedKey = _salt != null
            ? RestoreEngine.DeriveKeyV2(password, _salt)
            : RestoreEngine.DeriveKey(password);
        PasswordBox.Clear();
        ConfirmPasswordBox.Clear();
        DialogResult = true;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorBlock.Text = message;
        ErrorBlock.Visibility = Visibility.Visible;
    }

    /// <summary>Efface les champs de saisie à la fermeture pour ne pas laisser le mot de passe en mémoire.</summary>
    protected override void OnClosed(EventArgs e)
    {
        PasswordBox.Clear();
        ConfirmPasswordBox.Clear();
        base.OnClosed(e);
    }
}
