using Microsoft.Win32;
using System.Windows;
using WinBack.App.ViewModels;
using WinBack.Core.Services;

namespace WinBack.App.Views;

public partial class ProfileEditorWindow : Window
{
    private readonly ProfileEditorViewModel _vm;
    private readonly ProfileService _profileService;

    public ProfileEditorWindow(ProfileEditorViewModel vm, ProfileService profileService)
    {
        InitializeComponent();
        _vm = vm;
        _profileService = profileService;
        DataContext = vm;

        // Fermer automatiquement après sauvegarde
        PropertyChangedEventManager.AddHandler(vm, (s, e) =>
        {
            if (e.PropertyName == nameof(vm.IsBusy) && !vm.IsBusy && vm.Saved)
            {
                DialogResult = true;
                Close();
            }
        }, nameof(vm.IsBusy));
    }

    public void InitFromDrive(DriveDetails drive)
    {
        _vm.InitFromDrive(drive);
        TitleBlock.Text = "Configurer un nouveau disque";
    }

    public async void LoadProfileForEdit(int profileId)
    {
        var profiles = await _profileService.GetAllProfilesAsync();
        var profile = profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return;
        _vm.InitFromProfile(profile);
        TitleBlock.Text = $"Modifier — {profile.Name}";
    }

    private void BrowseSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PairRowViewModel pairVm })
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Choisir le dossier à sauvegarder",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            if (dialog.ShowDialog() == true)
            {
                pairVm.SourcePath = dialog.FolderName;
                // Proposer un nom de destination basé sur le nom du dossier source
                if (string.IsNullOrWhiteSpace(pairVm.DestRelativePath))
                    pairVm.DestRelativePath = Path.GetFileName(dialog.FolderName);
            }
        }
    }

    private void RemovePair_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PairRowViewModel pairVm })
            _vm.RemovePairCommand.Execute(pairVm);
    }
}
