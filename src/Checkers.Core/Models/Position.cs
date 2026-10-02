namespace Checkers.Core.Models;

/// <summary>
/// Represents a square coordinate on an 8x8 checkers board (0-indexed).
/// Row 0 is Black's home rank; Row 7 is White's home rank.
/// </summary>
public readonly record struct Position(int Row, int Col)
{
    public bool IsValid => Row is >= 0 and < 8 && Col is >= 0 and < 8;

    /// <summary>
    /// Playable dark squares satisfy (Row + Col) % 2 != 0.
    /// </summary>
    public bool IsDarkSquare => IsValid && (Row + Col) % 2 != 0;

    /// <summary>
    /// Returns an offset position by (dRow, dCol).
    /// </summary>
    public Position Offset(int dRow, int dCol) => new(Row + dRow, Col + dCol);

    /// <summary>
    /// Converts a dark square coordinate to standard Draughts notation (1 to 32).
    /// Returns null if the square is not a valid dark square.
    /// </summary>
    public int? ToDraughtsIndex()
    {
        if (!IsDarkSquare)
            return null;

        return Row * 4 + (Col / 2) + 1;
    }

    /// <summary>
    /// Converts standard Draughts square notation (1 to 32) into a Position coordinate.
    /// </summary>
    public static Position FromDraughtsIndex(int index)
    {
        if (index is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(index), "Draughts index must be between 1 and 32.");

        int zeroIndex = index - 1;
        int row = zeroIndex / 4;
        int colInRow = zeroIndex % 4;
        int col = (row % 2 == 0) ? (colInRow * 2 + 1) : (colInRow * 2);

        return new Position(row, col);
    }

    /// <summary>
    /// Safely attempts to convert standard Draughts notation (1 to 32) into a Position coordinate.
    /// </summary>
    public static bool TryFromDraughtsIndex(int index, out Position position)
    {
        if (index is >= 1 and <= 32)
        {
            position = FromDraughtsIndex(index);
            return true;
        }

        position = default;
        return false;
    }

    public override string ToString() => $"({Row},{Col})[#{ToDraughtsIndex()?.ToString() ?? "Light"}]";
}
