using Checkers.Core.AI;
using Checkers.Core.Models;

namespace Checkers.App.Models;

/// <summary>
/// Controls the checkers variant and how long the computer may think.
/// </summary>
public sealed record GameSettings(
    TimeControlMode Mode,
    int Depth,
    int SecondsPerMove,
    int MinutesPerGame,
    CheckersVariant Variant = CheckersVariant.International)
{
    public const int MaxDepth = 20;
    public const int MaxSecondsPerMove = 60;
    public const int MaxMinutesPerGame = 60;

    public static GameSettings Default { get; } = new(TimeControlMode.TimePerGame, 8, 5, 5, CheckersVariant.International);

    public TimeSpan GameTime => TimeSpan.FromMinutes(MinutesPerGame);

    /// <summary>Returns settings clamped to allowed bounds.</summary>
    public GameSettings Normalize() => new(
        Mode is TimeControlMode.FixedDepth or TimeControlMode.TimePerMove or TimeControlMode.TimePerGame ? Mode : Default.Mode,
        Math.Clamp(Depth, 1, MaxDepth),
        Math.Clamp(SecondsPerMove, 1, MaxSecondsPerMove),
        Math.Clamp(MinutesPerGame, 1, MaxMinutesPerGame),
        Variant is CheckersVariant.English ? CheckersVariant.English : CheckersVariant.International);

    public SearchLimits ToLimits(TimeSpan computerTimeLeft) => Mode switch
    {
        TimeControlMode.FixedDepth => SearchLimits.FixedDepth(Depth),
        TimeControlMode.TimePerMove => SearchLimits.TimePerMove(TimeSpan.FromSeconds(SecondsPerMove)),
        _ => SearchLimits.TimePerGame(computerTimeLeft > TimeSpan.Zero ? computerTimeLeft : TimeSpan.FromMilliseconds(10)),
    };

    public string Summary => Mode switch
    {
        TimeControlMode.FixedDepth => $"{Depth} plies",
        TimeControlMode.TimePerMove => $"{SecondsPerMove}s / move",
        _ => $"{MinutesPerGame} min / game",
    };

    public string VariantName => Variant switch
    {
        CheckersVariant.English => "English Checkers",
        _ => "International Draughts"
    };

    public string VariantBadgeText => Variant switch
    {
        CheckersVariant.English => "English",
        _ => "Flying Kings"
    };
}
