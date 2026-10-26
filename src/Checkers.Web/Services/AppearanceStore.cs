using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace Checkers.Web.Services;

public enum BoardStyle
{
    Walnut,
    GoldenOak,
    ReddishWood,
}

/// <summary>How the board looks in the browser; the desktop app keeps its own look.</summary>
public sealed record Appearance(BoardStyle Board)
{
    public static Appearance Default { get; } = new(BoardStyle.Walnut);
}

/// <summary>The appearance as JSON in local storage, apart from the settings shared with the desktop app.</summary>
public sealed class AppearanceStore(IJSInProcessRuntime js)
{
    private const string Key = "checkers.appearance";

    public Appearance Load()
    {
        try
        {
            string? json = js.Invoke<string?>("localStorage.getItem", Key);
            return json is null ? Appearance.Default : JsonSerializer.Deserialize(json, AppearanceJson.Default.Appearance) ?? Appearance.Default;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or JSException)
        {
            return Appearance.Default;
        }
    }

    public void Save(Appearance appearance)
    {
        try
        {
            js.InvokeVoid("localStorage.setItem", Key, JsonSerializer.Serialize(appearance, AppearanceJson.Default.Appearance));
        }
        catch (JSException)
        {
            // The look still changes; it is only not remembered.
        }
    }
}

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(Appearance))]
internal sealed partial class AppearanceJson : JsonSerializerContext;
