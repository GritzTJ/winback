using System.Windows;
using System.Windows.Controls;
using WinBack.App.ViewModels;

namespace WinBack.App.Views;

public partial class HistoryWindow : Window
{
    private readonly HistoryViewModel _vm;

    public HistoryWindow(HistoryViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await _vm.LoadCommand.ExecuteAsync(null);
    }

    private async void RunList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is BackupRunDetailViewModel run)
            await _vm.SelectRunCommand.ExecuteAsync(run);
    }
}
