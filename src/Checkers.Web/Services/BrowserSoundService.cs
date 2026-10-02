using Microsoft.JSInterop;
using Checkers.App.Models;
using Checkers.App.Services;

namespace Checkers.Web.Services;

/// <summary>
/// Plays procedural audio waves synthesized by SoundWaves in the browser via Web Audio API / HTML5 Audio.
/// </summary>
public sealed class BrowserSoundService(IJSInProcessRuntime js) : ISoundService
{
    private bool _registered;

    public bool IsEnabled { get; set; } = true;

    public void Play(SoundType sound)
    {
        if (!IsEnabled)
            return;

        try
        {
            if (!_registered)
            {
                foreach (SoundType each in Enum.GetValues<SoundType>())
                {
                    js.InvokeVoid("checkers.registerSound", each.ToString(), SoundWaves.Create(each));
                }

                _registered = true;
            }

            js.InvokeVoid("checkers.playSound", sound.ToString());
        }
        catch (JSException)
        {
            // Game functions silently if audio permissions are denied.
        }
    }
}
