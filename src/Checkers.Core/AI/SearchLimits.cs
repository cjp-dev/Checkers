namespace Checkers.Core.AI;

/// <summary>
/// Specifies how the computer player's thinking time or depth is constrained.
/// </summary>
public enum TimeControlMode
{
    /// <summary>Search to a fixed ply depth with no time constraint.</summary>
    FixedDepth,

    /// <summary>Allocates a fixed time budget for each individual move.</summary>
    TimePerMove,

    /// <summary>Allocates and shares a total game clock across all remaining moves.</summary>
    TimePerGame
}

/// <summary>
/// Encapsulates search limits (depth or time budget) passed to the AI player.
/// </summary>
public sealed record SearchLimits
{
    public TimeControlMode Mode { get; }

    /// <summary>Target depth for <see cref="TimeControlMode.FixedDepth"/>.</summary>
    public int Depth { get; }

    /// <summary>Time budget for the move, or remaining clock for the whole game.</summary>
    public TimeSpan Time { get; }

    /// <summary>Whether to query the opening book before running the tree search.</summary>
    public bool UseOpeningBook { get; init; }

    /// <summary>
    /// Maximum centipawn difference from the highest-scored book move within which alternative
    /// stored book moves may be chosen at random (default 10 cp).
    /// </summary>
    public int BookRandomMarginCp { get; init; } = Book.OpeningBook.DefaultRandomMarginCp;

    private SearchLimits(TimeControlMode mode, int depth, TimeSpan time, bool useOpeningBook = false)
    {
        Mode = mode;
        Depth = depth;
        Time = time;
        UseOpeningBook = useOpeningBook;
    }

    public static SearchLimits FixedDepth(int plies, bool useOpeningBook = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(plies, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(plies, 60);
        return new SearchLimits(TimeControlMode.FixedDepth, plies, TimeSpan.Zero, useOpeningBook);
    }

    public static SearchLimits TimePerMove(TimeSpan time, bool useOpeningBook = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(time, TimeSpan.Zero);
        return new SearchLimits(TimeControlMode.TimePerMove, 0, time, useOpeningBook);
    }

    public static SearchLimits TimePerGame(TimeSpan remaining, bool useOpeningBook = false) =>
        new(TimeControlMode.TimePerGame, 0, remaining, useOpeningBook);
}
