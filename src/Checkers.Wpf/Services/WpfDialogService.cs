using System.Windows;
using Checkers.App.Services;

namespace Checkers.Wpf.Services;

public sealed class WpfDialogService : IDialogService
{
    public void ShowInfo(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public bool ShowConfirmation(string title, string message)
    {
        return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    public void ShowAbout()
    {
        string message = 
            "Checkers (Draughts)\n" +
            "Version 1.0 (.NET 10 WPF)\n\n" +
            "Rules implemented:\n" +
            "• 8x8 Board with 12 pieces per player\n" +
            "• International Flying Kings (long-distance slides & jumps)\n" +
            "• Strict Mandatory Captures (Forced Jumps)\n" +
            "• Free Choice among valid capture lines\n" +
            "• Crown-row promotion ends turn immediately\n\n" +
            "Built with Clean Architecture & CommunityToolkit.Mvvm.";

        MessageBox.Show(message, "About Checkers", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
