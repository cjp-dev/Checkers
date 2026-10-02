using Checkers.App.Models;

namespace Checkers.App.Services;

public interface IDialogService
{
    void ShowInfo(string title, string message);
    bool ShowConfirmation(string title, string message);
    void ShowAbout();
    void ShowError(string message);
    Task<GameSettings?> EditSettingsAsync(GameSettings current);
}
