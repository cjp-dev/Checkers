using System.Windows;
using Checkers.App.ViewModels;
using Checkers.Wpf.Services;

namespace Checkers.Wpf;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var soundService = new WpfSoundService();
        var dialogService = new WpfDialogService();
        DataContext = new MainViewModel(soundService: soundService, dialogService: dialogService);
    }

    private void OnExitClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}