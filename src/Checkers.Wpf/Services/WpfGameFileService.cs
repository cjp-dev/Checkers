using System.IO;
using System.Windows;
using Microsoft.Win32;
using Checkers.App.Services;

namespace Checkers.Wpf.Services;

public sealed class WpfGameFileService : IGameFileService
{
    private const string GameFilter = "Checkers games (*.checkers)|*.checkers|Portable Draughts Notation (*.pdn)|*.pdn|Text files (*.txt)|*.txt|All files (*.*)|*.*";

    private static Window? Owner => Application.Current?.MainWindow;

    public async Task<GameFile?> OpenAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = GameFilter,
            DefaultExt = ".checkers"
        };

        if (dialog.ShowDialog(Owner) != true)
            return null;

        string text = await File.ReadAllTextAsync(dialog.FileName);
        return new GameFile(dialog.FileName, text);
    }

    public Task<string?> SaveAsync(string text, string? currentPath, bool askForName)
    {
        string? path = askForName || string.IsNullOrEmpty(currentPath)
            ? AskForPath(currentPath)
            : currentPath;

        if (path != null)
        {
            File.WriteAllText(path, text);
        }

        return Task.FromResult(path);
    }

    private static string? AskForPath(string? currentPath)
    {
        var dialog = new SaveFileDialog
        {
            Filter = GameFilter,
            DefaultExt = ".checkers",
            FileName = string.IsNullOrEmpty(currentPath) ? "Game" : Path.GetFileName(currentPath),
            InitialDirectory = string.IsNullOrEmpty(currentPath) ? "" : Path.GetDirectoryName(currentPath) ?? ""
        };

        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }
}
