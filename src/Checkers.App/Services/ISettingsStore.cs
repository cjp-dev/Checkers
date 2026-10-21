using Checkers.App.Models;

namespace Checkers.App.Services;

public sealed record AppPreferences(
    GameSettings Game,
    bool ShowAnalysis = true,
    bool SoundEnabled = true);

public interface ISettingsStore
{
    AppPreferences Load();
    bool Save(AppPreferences preferences);
}

