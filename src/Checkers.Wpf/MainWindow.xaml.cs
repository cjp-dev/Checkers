using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Checkers.App.ViewModels;
using Checkers.Wpf.Services;

namespace Checkers.Wpf;

public partial class MainWindow : Window
{
    public static readonly RoutedUICommand UserGuideCommand = new(
        "User guide", nameof(UserGuideCommand), typeof(MainWindow),
        new InputGestureCollection { new KeyGesture(Key.F1) });

    public static readonly RoutedUICommand DocumentationCommand = new(
        "Rules & Documentation", nameof(DocumentationCommand), typeof(MainWindow),
        new InputGestureCollection { new KeyGesture(Key.F1, ModifierKeys.Shift) });

    public MainWindow()
    {
        InitializeComponent();

        CommandBindings.Add(new CommandBinding(UserGuideCommand, OnUserGuideExecuted));
        CommandBindings.Add(new CommandBinding(DocumentationCommand, OnDocumentationExecuted));

        var soundService = new WpfSoundService();
        var dialogService = new WpfDialogService();
        var fileService = new WpfGameFileService();
        DataContext = new MainViewModel(soundService: soundService, dialogService: dialogService, fileService: fileService);
    }

    private void OnExitClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnUserGuideExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        OpenUrl("https://github.com/cjp-dev/Checkers/blob/main/docs/brain/14-gui-and-settings.md");
    }

    private void OnDocumentationExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        OpenUrl("https://github.com/cjp-dev/Checkers/blob/main/docs/brain/README.md");
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }
}
