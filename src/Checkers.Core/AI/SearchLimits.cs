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

    private SearchLimits(TimeControlMode mode, int depth, TimeSpan time)
    {
        Mode = mode;
        Depth = depth;
        Time = time;
    }

    public static SearchLimits FixedDepth(int plies)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(plies, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(plies, 60);
        return new SearchLimits(TimeControlMode.FixedDepth, plies, TimeSpan.Zero);
    }

    public static SearchLimits TimePerMove(TimeSpan time)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(time, TimeSpan.Zero);
        return new SearchLimits(TimeControlMode.TimePerMove, 0, time);
    }

    public static SearchLimits TimePerGame(TimeSpan remaining) =>
        new(TimeControlMode.TimePerGame, 0, remaining);
}
