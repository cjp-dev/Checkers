using System.Windows;
using Checkers.App.Models;
using Checkers.App.Services;
using Checkers.App.ViewModels;
using Checkers.Wpf.Views;

namespace Checkers.Wpf.Services;

public sealed class WpfDialogService : IDialogService
{
    private static Window? Owner => Application.Current?.MainWindow;

    public void ShowInfo(string title, string message)
    {
        if (Owner != null)
            MessageBox.Show(Owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public bool ShowConfirmation(string title, string message)
    {
        if (Owner != null)
            return MessageBox.Show(Owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        else
            return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    public Task<bool> ConfirmAsync(string title, string message) =>
        Task.FromResult(ShowConfirmation(title, message));

    public void ShowError(string message)
    {
        if (Owner != null)
            MessageBox.Show(Owner, message, "Checkers", MessageBoxButton.OK, MessageBoxImage.Error);
        else
            MessageBox.Show(message, "Checkers", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public void ShowAbout()
    {
        string message = $"{AboutInfo.Version}\n\n" +
            string.Join("\n\n", AboutInfo.Paragraphs) +
            $"\n\n{AboutInfo.PictureCaption}";

        if (Owner != null)
            MessageBox.Show(Owner, message, AboutInfo.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show(message, AboutInfo.Title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public Task<GameSettings?> EditSettingsAsync(GameSettings current)
    {
        var viewModel = new SettingsViewModel(current);
        var window = new SettingsWindow { Owner = Owner, DataContext = viewModel };
        return Task.FromResult(window.ShowDialog() == true ? viewModel.ToSettings() : null);
    }
}
