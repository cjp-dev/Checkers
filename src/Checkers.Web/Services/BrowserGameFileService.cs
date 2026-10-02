using Microsoft.JSInterop;
using Checkers.App.Services;

namespace Checkers.Web.Services;

/// <summary>
/// Opens a game using the browser's file picker and saves games as browser downloads.
/// </summary>
public sealed class BrowserGameFileService(IJSRuntime js, BrowserDialogService dialogs) : IGameFileService
{
    private const string DefaultExtension = ".checkers";

    public async Task<GameFile?> OpenAsync()
    {
        string[]? file;
        try
        {
            file = await js.InvokeAsync<string[]?>("checkers.openTextFile", DefaultExtension);
        }
        catch (JSException exception)
        {
            throw new IOException(exception.Message, exception);
        }

        return file is [string name, string text] ? new GameFile(name, text) : null;
    }

    public async Task<string?> SaveAsync(string text, string? currentName, bool askForName)
    {
        string? name = currentName;
        if (askForName || name is null)
        {
            name = (await dialogs.AskTextAsync("Save the game as:", currentName ?? "Game" + DefaultExtension))?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (Path.GetExtension(name).Length == 0)
            {
                name += DefaultExtension;
            }
        }

        await js.InvokeVoidAsync("checkers.downloadTextFile", name, text);
        return name;
    }
}
