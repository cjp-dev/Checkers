namespace Checkers.Core.Models;

/// <summary>
/// Represents a fully resolved, atomic player move (which may consist of a single step or a multi-jump sequence).
/// </summary>
public sealed record Move
{
    public required Position From { get; init; }
    public required Position To { get; init; }
    public required IReadOnlyList<Position> Path { get; init; }
    public required IReadOnlyList<Position> CapturedPositions { get; init; }
    public bool IsPromotion { get; init; }

    public bool IsCapture => CapturedPositions.Count > 0;
    public string Notation { get; init; } = string.Empty;

    public static Move CreateQuiet(Position from, Position to, bool isPromotion = false)
    {
        string notation = FormatNotation([from, to], isCapture: false);
        return new Move
        {
            From = from,
            To = to,
            Path = [from, to],
            CapturedPositions = [],
            IsPromotion = isPromotion,
            Notation = notation
        };
    }

    public static Move CreateCapture(
        Position from,
        Position to,
        IReadOnlyList<Position> path,
        IReadOnlyList<Position> capturedPositions,
        bool isPromotion = false)
    {
        string notation = FormatNotation(path, isCapture: true);
        return new Move
        {
            From = from,
            To = to,
            Path = path,
            CapturedPositions = capturedPositions,
            IsPromotion = isPromotion,
            Notation = notation
        };
    }

    private static string FormatNotation(IReadOnlyList<Position> path, bool isCapture)
    {
        char separator = isCapture ? 'x' : '-';
        var parts = path.Select(p => p.ToDraughtsIndex()?.ToString() ?? $"({p.Row},{p.Col})");
        return string.Join(separator, parts);
    }

    public override string ToString() => Notation;
}
