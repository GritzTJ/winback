using System.IO;
using System.Windows;
using WinBack.App.ViewModels;

namespace WinBack.App.Views;

public partial class DashboardWindow : Window
{
    private readonly DashboardViewModel _vm;

    public DashboardWindow(DashboardViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await _vm.LoadCommand.ExecuteAsync(null);
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // Réduire dans la barre système plutôt que quitter
        e.Cancel = true;
        Hide();
    }

    private void OpenRestore_Click(object sender, RoutedEventArgs e)
    {
        // RestoreWindow est Transient : une nouvelle instance à chaque ouverture
        var win = App.GetService<RestoreWindow>();
        win.Owner = this;
        win.ShowDialog();
    }

    private void OpenHistory_Click(object sender, RoutedEventArgs e)
    {
        var win = App.GetService<HistoryWindow>();
        win.Show();
        win.Activate();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var win = App.GetService<SettingsWindow>();
        win.ShowDialog();
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var win = App.GetService<ProfileEditorWindow>();
        if (win.ShowDialog() == true)
            _ = _vm.LoadCommand.ExecuteAsync(null);
    }

    private void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int profileId })
        {
            var win = App.GetService<ProfileEditorWindow>();
            win.LoadProfileForEdit(profileId);
            if (win.ShowDialog() == true)
                _ = _vm.LoadCommand.ExecuteAsync(null);
        }
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int profileId })
        {
            var result = MessageBox.Show(
                "Supprimer ce profil de sauvegarde ?\n\nLes fichiers déjà sauvegardés ne seront pas supprimés.",
                "Confirmer la suppression",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
                await _vm.DeleteProfileCommand.ExecuteAsync(profileId);
        }
    }

    private async void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int profileId }) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "Exporter le profil WinBack",
            Filter     = "Profil WinBack (*.winback.json)|*.winback.json",
            DefaultExt = ".winback.json"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var json = await _vm.ExportProfileAsync(profileId);
            await File.WriteAllTextAsync(dialog.FileName, json);
            MessageBox.Show($"Profil exporté :\n{dialog.FileName}",
                "WinBack — Export réussi",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossible d'exporter le profil :\n{ex.Message}",
                "WinBack — Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Importer un profil WinBack",
            Filter = "Profil WinBack (*.winback.json)|*.winback.json"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var json = await File.ReadAllTextAsync(dialog.FileName);
            await _vm.ImportProfileAsync(json);
            MessageBox.Show("Profil importé avec succès.",
                "WinBack — Import réussi",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossible d'importer le profil :\n{ex.Message}",
                "WinBack — Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
