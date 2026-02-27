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
}
