using System.Numerics;
using System.Runtime.CompilerServices;
using Checkers.Core.AI;
using Checkers.Core.Models;

namespace Checkers.Core.Bitboards;

/// <summary>
/// Hardware popcount-accelerated static evaluation function operating directly on 64-bit bitboards.
/// Produces the exact same scores as <see cref="EvaluationFunction"/> for both International and English Checkers.
/// </summary>
public sealed class BitboardEvaluation : IEvaluationFunction
{
    private const ulong Row0 = 0x00000000000000FFUL;
    private const ulong Row1 = 0x000000000000FF00UL;
    private const ulong Row2 = 0x0000000000FF0000UL;
    private const ulong Row3 = 0x00000000FF000000UL;
    private const ulong Row4 = 0x000000FF00000000UL;
    private const ulong Row5 = 0x0000FF0000000000UL;
    private const ulong Row6 = 0x00FF000000000000UL;
    private const ulong Row7 = 0xFF00000000000000UL;

    public CheckersVariant Variant { get; }

    public BitboardEvaluation(CheckersVariant variant = CheckersVariant.International)
    {
        Variant = variant;
    }

    public int Evaluate(BoardState state)
    {
        var pos = BitPosition.FromBoardState(state);
        return Evaluate(in pos, Variant);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Evaluate(in BitPosition pos, CheckersVariant variant)
    {
        bool isEnglish = variant == CheckersVariant.English;
        int kingVal = isEnglish ? EvaluationFunction.EnglishKingValue : EvaluationFunction.InternationalKingValue;
        int advanceStep = isEnglish ? EvaluationFunction.EnglishAdvancementStepBonus : EvaluationFunction.AdvancementStepBonus;

        ulong wm = pos.WhiteMen;
        ulong wk = pos.WhiteKings;
        ulong bm = pos.BlackMen;
        ulong bk = pos.BlackKings;
        ulong white = wm | wk;
        ulong black = bm | bk;

        // 1. Material
        int whiteScore = BitOperations.PopCount(wm) * EvaluationFunction.ManValue
                       + BitOperations.PopCount(wk) * kingVal;
        int blackScore = BitOperations.PopCount(bm) * EvaluationFunction.ManValue
                       + BitOperations.PopCount(bk) * kingVal;

        // 2. Center Control (rows 3..4, cols 2..5)
        whiteScore += BitOperations.PopCount(white & BitboardMasks.CenterMask) * EvaluationFunction.CenterControlBonus;
        blackScore += BitOperations.PopCount(black & BitboardMasks.CenterMask) * EvaluationFunction.CenterControlBonus;

        // 3. King Centralization (English Checkers only: rows 2..5, cols 2..5)
        if (isEnglish)
        {
            whiteScore += BitOperations.PopCount(wk & BitboardMasks.KingCenterMask) * EvaluationFunction.KingCentralizationBonus;
            blackScore += BitOperations.PopCount(bk & BitboardMasks.KingCenterMask) * EvaluationFunction.KingCentralizationBonus;
        }

        // 4. Back rank defense (White on Row 7, Black on Row 0)
        whiteScore += BitOperations.PopCount(wm & Row7) * EvaluationFunction.BackRankDefenseBonus;
        blackScore += BitOperations.PopCount(bm & Row0) * EvaluationFunction.BackRankDefenseBonus;

        // 5. Man Advancement (White advances toward Row 0: weight = 7 - r; Black advances toward Row 7: weight = r)
        int whiteAdvanceSteps =
              BitOperations.PopCount(wm & Row6) * 1
            + BitOperations.PopCount(wm & Row5) * 2
            + BitOperations.PopCount(wm & Row4) * 3
            + BitOperations.PopCount(wm & Row3) * 4
            + BitOperations.PopCount(wm & Row2) * 5
            + BitOperations.PopCount(wm & Row1) * 6
            + BitOperations.PopCount(wm & Row0) * 7;

        int blackAdvanceSteps =
              BitOperations.PopCount(bm & Row1) * 1
            + BitOperations.PopCount(bm & Row2) * 2
            + BitOperations.PopCount(bm & Row3) * 3
            + BitOperations.PopCount(bm & Row4) * 4
            + BitOperations.PopCount(bm & Row5) * 5
            + BitOperations.PopCount(bm & Row6) * 6
            + BitOperations.PopCount(bm & Row7) * 7;

        whiteScore += whiteAdvanceSteps * advanceStep;
        blackScore += blackAdvanceSteps * advanceStep;

        int netScore = whiteScore - blackScore;
        return pos.SideToMove == PieceColor.White ? netScore : -netScore;
    }
}
