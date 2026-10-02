namespace Checkers.App.Services;

/// <summary>
/// Opens and saves game records. On desktop (WPF), a name is a full file path; in Web, a file name.
/// </summary>
public interface IGameFileService
{
    /// <summary>
    /// Prompts the user to select and open a saved game file.
    /// </summary>
    Task<GameFile?> OpenAsync();

    /// <summary>
    /// Saves the game record text. If askForName is true or currentPath is null, prompts the user for a destination.
    /// </summary>
    Task<string?> SaveAsync(string text, string? currentPath, bool askForName);
}

public sealed record GameFile(string Path, string Text);
