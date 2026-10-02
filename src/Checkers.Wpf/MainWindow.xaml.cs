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
        var fileService = new WpfGameFileService();
        DataContext = new MainViewModel(soundService: soundService, dialogService: dialogService, fileService: fileService);
    }

    private void OnExitClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}