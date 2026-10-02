using System.IO;
using System.Media;
using Checkers.App.Models;
using Checkers.App.Services;

namespace Checkers.Wpf.Services;

public sealed class WpfSoundService : ISoundService
{
    public bool IsEnabled { get; set; } = true;

    public void Play(SoundType sound)
    {
        if (!IsEnabled)
            return;

        try
        {
            byte[] wavBytes = SoundWaves.Create(sound);
            using var stream = new MemoryStream(wavBytes);
            using var player = new SoundPlayer(stream);
            player.Play();
        }
        catch
        {
            // Ignore sound errors on systems without audio devices
        }
    }
}
