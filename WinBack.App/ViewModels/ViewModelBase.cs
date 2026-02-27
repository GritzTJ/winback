using CommunityToolkit.Mvvm.ComponentModel;

namespace WinBack.App.ViewModels;

/// <summary>
/// Classe de base pour tous les ViewModels.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    protected void SetBusy(bool busy, string message = "")
    {
        IsBusy = busy;
        StatusMessage = message;
    }
}
