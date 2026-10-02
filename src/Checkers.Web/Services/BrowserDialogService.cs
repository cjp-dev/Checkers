using Microsoft.JSInterop;
using Checkers.App.Models;
using Checkers.App.Services;
using Checkers.App.ViewModels;

namespace Checkers.Web.Services;

/// <summary>
/// Modal dialogs shown one at a time by the DialogHost component.
/// </summary>
public sealed class BrowserDialogService(IJSInProcessRuntime js) : IDialogService
{
    private const string Caption = "Checkers";

    private readonly Queue<DialogRequest> _queue = new();

    public event Action? Changed;

    public DialogRequest? Current => _queue.TryPeek(out DialogRequest? request) ? request : null;

    public async Task<GameSettings?> EditSettingsAsync(GameSettings current)
    {
        var settings = new SettingsViewModel(current);
        int button = await ShowAsync(new DialogRequest { Title = "Settings", Settings = settings, Buttons = ["OK", "Cancel"] });
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

    /// <summary>Closes the dialog if it is still the one shown.</summary>
    public void Close(DialogRequest request, int button)
    {
        if (Current != request)
        {
            return;
        }

        _queue.Dequeue();
        request.Complete(button);
        Changed?.Invoke();
    }

    private Task<int> ShowAsync(DialogRequest request)
    {
        _queue.Enqueue(request);
        Changed?.Invoke();
        return request.Result;
    }
}

/// <summary>The first button is the default (Enter), the last one cancels (Escape).</summary>
public sealed class DialogRequest
{
    private readonly TaskCompletionSource<int> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public required string Title { get; init; }

    public string? Heading { get; init; }

    public string? Message { get; init; }

    public IReadOnlyList<string> Paragraphs { get; init; } = [];

    /// <summary>A picture shown to the left of the text, with a caption below it.</summary>
    public string? Picture { get; init; }

    public string? PictureCaption { get; init; }

    public SettingsViewModel? Settings { get; init; }

    /// <summary>The text in the input box; null for dialogs without one.</summary>
    public string? Text { get; set; }

    public required IReadOnlyList<string> Buttons { get; init; }

    internal Task<int> Result => _result.Task;

    internal void Complete(int button) => _result.TrySetResult(button);
}
