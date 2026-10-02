using System.Windows;

namespace Checkers.Wpf.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
