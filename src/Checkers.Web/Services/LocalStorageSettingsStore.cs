using System.Text.Json;
using Checkers.App.Models;
using Checkers.App.Services;
using Checkers.Web.Worker;
using Microsoft.JSInterop;

namespace Checkers.Web.Services;

public sealed class LocalStorageSettingsStore : ISettingsStore
{
    private const string Key = "checkers.settings";
    private readonly IJSInProcessRuntime _js;

    public LocalStorageSettingsStore(IJSInProcessRuntime js)
    {
        _js = js;
    }

    public AppPreferences Load()
    {
        try
        {
            string? json = _js.Invoke<string?>("localStorage.getItem", Key);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var prefs = JsonSerializer.Deserialize(json, EngineProtocolJson.Default.AppPreferences);
                if (prefs != null)
                {
                    return prefs;
                }
            }
        }
        catch
        {
            // Silently fall back to defaults if localStorage is unavailable or corrupted
        }

        return new AppPreferences(GameSettings.Default, ShowAnalysis: true, SoundEnabled: true);
    }

    public bool Save(AppPreferences preferences)
    {
        try
        {
            string json = JsonSerializer.Serialize(preferences, EngineProtocolJson.Default.AppPreferences);
            _js.InvokeVoid("localStorage.setItem", Key, json);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

