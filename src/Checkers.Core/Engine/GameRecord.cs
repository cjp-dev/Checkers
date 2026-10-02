namespace Checkers.Core.Engine;

/// <summary>
/// Represents a parsed game record containing a replayed GameSession and any metadata tags.
/// </summary>
public sealed record GameRecord
{
    public required GameSession Session { get; init; }
    public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();

    public string? GetTag(string key) => Tags.TryGetValue(key, out var val) ? val : null;
}
