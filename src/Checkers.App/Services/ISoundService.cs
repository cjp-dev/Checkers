using Checkers.App.Models;

namespace Checkers.App.Services;

public interface ISoundService
{
    bool IsEnabled { get; set; }
    void Play(SoundType sound);
}
