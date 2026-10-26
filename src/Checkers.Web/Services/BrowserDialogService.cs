using Microsoft.JSInterop;
using Checkers.App.Models;
using Checkers.App.Services;
using Checkers.App.ViewModels;
using Checkers.Web.Components;
using GameShell;

namespace Checkers.Web.Services;

/// <summary>
/// Modal dialogs shown one at a time by the DialogHost component.
/// </summary>
public sealed class BrowserDialogService(IJSInProcessRuntime js, ShellDialogs dialogs) : IDialogService
{
    private const string Caption = "Checkers";

    public async Task<GameSettings?> EditSettingsAsync(GameSettings current)
    {
        var settings = new SettingsViewModel(current);
        int button = await ShowAsync(new DialogRequest { Title = "Settings", Body = SettingsForm.For(settings), Buttons = ["OK", "Cancel"] });
        return button == 0 ? settings.ToSettings() : null;
    }

    public void ShowInfo(string title, string message) =>
        _ = ShowAsync(new DialogRequest { Title = title, Message = message, Buttons = ["OK"] });

    public bool ShowConfirmation(string title, string message)
    {
        // For synchronous confirmation in WebAssembly, we can queue or return true
        _ = ShowAsync(new DialogRequest { Title = title, Message = message, Buttons = ["OK"] });
        return true;
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        int button = await ShowAsync(new DialogRequest { Title = title, Message = message, Buttons = ["Yes", "No"] });
        return button == 0;
    }

    public void ShowError(string message) =>
        _ = ShowAsync(new DialogRequest { Title = Caption, Message = message, Buttons = ["OK"] });

    public void ShowAbout() => _ = ShowAsync(new DialogRequest
    {
        Title = AboutInfo.Title,
        Heading = AboutInfo.Version,
        Paragraphs = AboutInfo.Paragraphs,
        Picture = "images/claus-pedersen.jpg",
        PictureCaption = AboutInfo.PictureCaption,
        Buttons = ["OK"],
    });

    public void Beep() => js.InvokeVoid("checkers.beep");

    /// <returns>The text the user entered, or null if the user cancelled.</returns>
    public async Task<string?> AskTextAsync(string message, string text)
    {
        var request = new DialogRequest { Title = Caption, Message = message, Text = text, Buttons = ["OK", "Cancel"] };
        return await ShowAsync(request) == 0 ? request.Text : null;
    }

    private Task<int> ShowAsync(DialogRequest request) => dialogs.ShowAsync(request);
}
