using System.Globalization;
using System.Numerics;
using Checkers.Core.AI;
using Checkers.Core.Models;

namespace Checkers.App.Models;

/// <summary>
/// Controls the checkers variant, transposition table size, and how long the computer may think.
/// </summary>
public sealed record GameSettings(
    TimeControlMode Mode,
    int Depth,
    int SecondsPerMove,
    int MinutesPerGame,
    CheckersVariant Variant = CheckersVariant.International,
    int TranspositionTableEntries = GameSettings.DefaultTranspositionTableEntries,
    bool UseOpeningBook = true)
{
    public const int MaxDepth = 20;
    public const int MaxSecondsPerMove = 60;
    public const int MaxMinutesPerGame = 60;
    public const int DefaultTranspositionTableEntries = TranspositionTable.DefaultEntries; // 1,048,576 (2^20)
    public const int MaxTranspositionTableEntries = TranspositionTable.MaxEntries;         // 16,777,216 (2^24)
    public const int MinTranspositionTablePower = 20;
    public const int MaxTranspositionTablePower = 24;

    public static GameSettings Default { get; } = new(
        TimeControlMode.TimePerGame,
        8,
        5,
        5,
        CheckersVariant.International,
        DefaultTranspositionTableEntries,
        UseOpeningBook: true);

    public TimeSpan GameTime => TimeSpan.FromMinutes(MinutesPerGame);

    /// <summary>Returns settings clamped to allowed bounds.</summary>
    public GameSettings Normalize()
    {
        int clampedTt = Math.Clamp(TranspositionTableEntries, DefaultTranspositionTableEntries, MaxTranspositionTableEntries);
        int normalizedTt = (int)BitOperations.RoundUpToPowerOf2((ulong)clampedTt);
        normalizedTt = Math.Clamp(normalizedTt, DefaultTranspositionTableEntries, MaxTranspositionTableEntries);

        return new(
            Mode is TimeControlMode.FixedDepth or TimeControlMode.TimePerMove or TimeControlMode.TimePerGame ? Mode : Default.Mode,
            Math.Clamp(Depth, 1, MaxDepth),
            Math.Clamp(SecondsPerMove, 1, MaxSecondsPerMove),
            Math.Clamp(MinutesPerGame, 1, MaxMinutesPerGame),
            Variant is CheckersVariant.English ? CheckersVariant.English : CheckersVariant.International,
            normalizedTt,
            UseOpeningBook);
    }

    public SearchLimits ToLimits(TimeSpan computerTimeLeft) => Mode switch
    {
        TimeControlMode.FixedDepth => SearchLimits.FixedDepth(Depth, UseOpeningBook),
        TimeControlMode.TimePerMove => SearchLimits.TimePerMove(TimeSpan.FromSeconds(SecondsPerMove), UseOpeningBook),
        _ => SearchLimits.TimePerGame(computerTimeLeft > TimeSpan.Zero ? computerTimeLeft : TimeSpan.FromMilliseconds(10), UseOpeningBook),
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

    public string TranspositionTableFormatted =>
        $"{TranspositionTableEntries.ToString("N0", CultureInfo.InvariantCulture)} entries";
}
