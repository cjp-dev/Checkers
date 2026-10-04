using System.Numerics;
using System.Runtime.CompilerServices;
using Checkers.Core.Bitboards;
using Checkers.Core.Models;

namespace Checkers.Core.AI;

/// <summary>
/// Hardware popcount-accelerated static heuristic evaluation function operating directly on 64-bit bitboards.
/// Implements a 1:1 2x-scaled centipawn port of <c>board_eval.c</c> for English Checkers (1-step Kings)
/// and an adapted Flying Kings evaluation for International Checkers (8x8 Flying Kings).
/// </summary>
public sealed class EvaluationFunction : IEvaluationFunction
{
    // Base Material (2x scale of board_eval.c: Man = 50*2 = 100, EnglishKing = 70*2 = 140, FlyingKing = 300)
    public const int ManValue = 100;
    public const int EnglishKingValue = 140;
    public const int InternationalKingValue = 300;
    public const int KingValue = InternationalKingValue; // Backwards compatibility

    // Legacy constants retained for binary/source compatibility
    public const int KingCentralizationBonus = 10;
    public const int CenterControlBonus = 12;
    public const int BackRankDefenseBonus = 15;
    public const int AdvancementStepBonus = 5;
    public const int EnglishAdvancementStepBonus = 6;

    // Row masks
    private const ulong Row0 = 0x00000000000000FFUL;
    private const ulong Row1 = 0x000000000000FF00UL;
    private const ulong Row2 = 0x0000000000FF0000UL;
    private const ulong Row3 = 0x00000000FF000000UL;
    private const ulong Row4 = 0x000000FF00000000UL;
    private const ulong Row5 = 0x0000FF0000000000UL;
    private const ulong Row6 = 0x00FF000000000000UL;
    private const ulong Row7 = 0xFF00000000000000UL;
    private const ulong CenterMask = 0x00003C3C3C3C0000UL;

    // Column masks for tail_pins (board_eval.c:396-397)
    private const ulong Cols0To5 = 0x3F3F3F3F3F3F3F3FUL;
    private const ulong Cols2To7 = 0xFCFCFCFCFCFCFCFCUL;

    // Man Piece-Square Table bitmasks (board_eval.c:515-542, compute_piece_pos_p1 & p2)
    // Weight 1 (+2 cp): (3,2)=26, (3,4)=28, (4,3)=35, (4,5)=37, (5,2)=42, (5,4)=44, (6,7)=55
    private const ulong WhiteManPst1Mask = 0x0080142814000000UL;
    // Weight 2 (+4 cp): (2,5)=21
    private const ulong WhiteManPst2Mask = 0x0000000000200000UL;
    // Weight 3 (+6 cp): (2,3)=19, (7,2)=58, (7,4)=60, (7,6)=62
    private const ulong WhiteManPst3Mask = 0x5400000000080000UL;

    // Black Man PST (180-degree rotated: 63 - sq)
    // Weight 1 (+2 cp): (4,5)=37, (4,3)=35, (3,4)=28, (3,2)=26, (2,5)=21, (2,3)=19, (1,0)=8
    private const ulong BlackManPst1Mask = 0x0000002814280100UL;
    // Weight 2 (+4 cp): (5,2)=42
    private const ulong BlackManPst2Mask = 0x0000040000000000UL;
    // Weight 3 (+6 cp): (5,4)=44, (0,5)=5, (0,3)=3, (0,1)=1
    private const ulong BlackManPst3Mask = 0x000010000000002AUL;

    // English King Piece-Square Table bitmasks (board_eval.c:544-563, compute_king_pos)
    // Weight 4 (+8 cp): (2,5)=21, (3,4)=28, (4,3)=35, (5,2)=42
    private const ulong EnglishKingPst4Mask = 0x0000040810200000UL;
    // Weight 5 (+10 cp): (1,2)=10, (2,1)=17, (2,3)=19, (3,2)=26, (4,5)=37, (5,4)=44, (5,6)=46, (6,5)=53
    private const ulong EnglishKingPst5Mask = 0x00205020040A0400UL;

    // English single-corner King trap squares (board_eval.c:316-322)
    private const ulong WhiteSingleCornerKingMask = 0x0000000000000080UL; // (0,7)
    private const ulong BlackSingleCornerKingMask = 0x0100000000000000UL; // (7,0)

    // International (Flying Kings) PST bitmasks
    // 8-square Main Long Diagonal: (0,7)=7, (1,6)=14, (2,5)=21, (3,4)=28, (4,3)=35, (5,2)=42, (6,1)=49, (7,0)=56 (+12 cp)
    private const ulong FlyingKingMainDiagonalMask = 0x0102040810204080UL;
    // Central & 6-square Double-Diagonal inner squares (+8 cp)
    private const ulong FlyingKingInnerMask = 0x00205020040A0400UL | 0x0008004002001000UL;
    // Cramped 2-square short corners: (0,1)=1, (1,0)=8, (6,7)=55, (7,6)=62 (-20 cp)
    private const ulong FlyingKingShortCornerMask = 0x4080000000000102UL;

    // Classical Checkers Structural Pattern Masks (board_eval.c:276-314)
    private const ulong RightLockWhiteMan = 0x0000008000000000UL;
    private const ulong RightLockBlackVictim = 0x0000000040000000UL;
    private const ulong RightLockBlackMan = 0x0000000001000000UL;
    private const ulong RightLockWhiteVictim = 0x0000000200000000UL;

    private const ulong TriangleWhiteMask = 0x5020000000000000UL;
    private const ulong TriangleBlackMask = 0x000000000000040AUL;

    private const ulong OreoWhiteMask = 0x1408000000000000UL;
    private const ulong OreoBlackMask = 0x0000000000001028UL;

    private const ulong BridgeWhiteMask = 0x4400000000000000UL;
    private const ulong BridgeBlackMask = 0x0000000000000022UL;

    private const ulong DogWhiteMan = 0x4000000000000000UL;
    private const ulong DogBlackVictim = 0x0080000000000000UL;
    private const ulong DogBlackMan = 0x0000000000000002UL;
    private const ulong DogWhiteVictim = 0x0000000000000100UL;

    // Precomputed forward promotion cones for Runaway Checker detection (board_eval.c:604-634)
    public static readonly ulong[] ConeWhite = ComputeRunawayCones(PieceColor.White);
    public static readonly ulong[] ConeBlack = ComputeRunawayCones(PieceColor.Black);

    public CheckersVariant Variant { get; }

    public EvaluationFunction(CheckersVariant variant = CheckersVariant.International)
    {
        Variant = variant;
    }

    public int Evaluate(BoardState state)
    {
        var pos = state.BitPosition;
        return Evaluate(in pos, Variant);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Evaluate(in BitPosition pos) => Evaluate(in pos, Variant);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Evaluate(in BitPosition pos, CheckersVariant variant)
    {
        return variant == CheckersVariant.English
            ? EvaluateEnglish(in pos)
            : EvaluateInternational(in pos);
    }

    /// <summary>
    /// 1:1 2x-scaled centipawn port of <c>board_eval.c</c> for English Checkers (1-step Kings, forward-only men captures).
    /// Proven +143.1 Elo (+41 =57 -2) over LegacyEvaluationFunction in 100-game self-play.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EvaluateEnglish(in BitPosition pos)
    {
        ulong wm = pos.WhiteMen;
        ulong wk = pos.WhiteKings;
        ulong bm = pos.BlackMen;
        ulong bk = pos.BlackKings;
        ulong white = wm | wk;
        ulong black = bm | bk;
        ulong allPieces = white | black;

        int wmCount = BitOperations.PopCount(wm);
        int wkCount = BitOperations.PopCount(wk);
        int bmCount = BitOperations.PopCount(bm);
        int bkCount = BitOperations.PopCount(bk);

        int whiteCount = wmCount + wkCount;
        int blackCount = bmCount + bkCount;
        int totalPieces = whiteCount + blackCount;

        // 1. Base Material (Man = 100, English King = 140)
        int whiteScore = (wmCount * ManValue) + (wkCount * EnglishKingValue);
        int blackScore = (bmCount * ManValue) + (bkCount * EnglishKingValue);

        // 2. Man Piece-Square Table (board_eval.c: compute_piece_pos_p1 & compute_piece_pos_p2, 2x scaled)
        whiteScore += (BitOperations.PopCount(wm & WhiteManPst1Mask) * 2)
                    + (BitOperations.PopCount(wm & WhiteManPst2Mask) * 4)
                    + (BitOperations.PopCount(wm & WhiteManPst3Mask) * 6);

        blackScore += (BitOperations.PopCount(bm & BlackManPst1Mask) * 2)
                    + (BitOperations.PopCount(bm & BlackManPst2Mask) * 4)
                    + (BitOperations.PopCount(bm & BlackManPst3Mask) * 6);

        // 3. Late-Game Man Advancement (king_dist: active only when totalPieces <= 12, 2x scaled)
        if (totalPieces <= 12)
        {
            int whiteAdvance =
                  BitOperations.PopCount(wm & Row6) * 1
                + BitOperations.PopCount(wm & Row5) * 2
                + BitOperations.PopCount(wm & Row4) * 3
                + BitOperations.PopCount(wm & Row3) * 4
                + BitOperations.PopCount(wm & Row2) * 5
                + BitOperations.PopCount(wm & Row1) * 6
                + BitOperations.PopCount(wm & Row0) * 7;

            int blackAdvance =
                  BitOperations.PopCount(bm & Row1) * 1
                + BitOperations.PopCount(bm & Row2) * 2
                + BitOperations.PopCount(bm & Row3) * 3
                + BitOperations.PopCount(bm & Row4) * 4
                + BitOperations.PopCount(bm & Row5) * 5
                + BitOperations.PopCount(bm & Row6) * 6
                + BitOperations.PopCount(bm & Row7) * 7;

            whiteScore += whiteAdvance * 2;
            blackScore += blackAdvance * 2;
        }

        // 4. Runaway Checkers / Unstoppable Passers (board_eval.c:223-240, 2x scaled: 40 + 6 * advance)
        if (bk == 0UL && wm != 0UL)
        {
            ulong candidateWm = bm == 0UL
                ? wm
                : wm & ((1UL << ((BitOperations.TrailingZeroCount(bm) & ~7) + 8)) - 1UL);

            while (candidateWm != 0UL)
            {
                int sq = BitOperations.TrailingZeroCount(candidateWm);
                candidateWm &= candidateWm - 1UL;
                if ((ConeWhite[sq] & allPieces) == 0UL)
                {
                    whiteScore += 40 + (6 * (7 - (sq >> 3)));
                }
            }
        }

        if (wk == 0UL && bm != 0UL)
        {
            ulong candidateBm = wm == 0UL
                ? bm
                : bm & ~((1UL << ((63 - BitOperations.LeadingZeroCount(wm)) & ~7)) - 1UL);

            while (candidateBm != 0UL)
            {
                int sq = BitOperations.TrailingZeroCount(candidateBm);
                candidateBm &= candidateBm - 1UL;
                if ((ConeBlack[sq] & allPieces) == 0UL)
                {
                    blackScore += 40 + (6 * (sq >> 3));
                }
            }
        }

        // 5. King Tail Pins (board_eval.c:395-406, 2x scaled: +10 cp per pin)
        if ((wk | bk) != 0UL)
        {
            ulong whitePins = ((wk & Cols0To5) & (bm >> 9) & (bm >> 18))
                            | ((wk & Cols2To7) & (bm >> 7) & (bm >> 14));
            ulong blackPins = ((bk & Cols2To7) & (wm << 9) & (wm << 18))
                            | ((bk & Cols0To5) & (wm << 7) & (wm << 14));

            whiteScore += BitOperations.PopCount(whitePins) * 10;
            blackScore += BitOperations.PopCount(blackPins) * 10;
        }

        // 6. English King PST + Trapped Single-Corner Penalty
        if (wk != 0UL)
        {
            whiteScore += (BitOperations.PopCount(wk & EnglishKingPst4Mask) * 8)
                        + (BitOperations.PopCount(wk & EnglishKingPst5Mask) * 10);
            if ((wk & WhiteSingleCornerKingMask) != 0UL)
                whiteScore -= 40;
        }
        if (bk != 0UL)
        {
            blackScore += (BitOperations.PopCount(bk & EnglishKingPst4Mask) * 8)
                        + (BitOperations.PopCount(bk & EnglishKingPst5Mask) * 10);
            if ((bk & BlackSingleCornerKingMask) != 0UL)
                blackScore -= 40;
        }

        // 7. Piece-Advantage Simplification Bonus (board_eval.c:265-272, 342-349, 2x scaled)
        if (whiteCount > blackCount)
        {
            whiteScore += CalculatePieceBonus(whiteCount);
        }
        else if (blackCount > whiteCount)
        {
            blackScore += CalculatePieceBonus(blackCount);
        }

        // 8. Classical Checkers Structural Patterns (board_eval.c:276-314, 2x scaled)
        if ((wm & RightLockWhiteMan) != 0UL && (bm & RightLockBlackVictim) != 0UL)
            whiteScore += 40;
        if ((bm & RightLockBlackMan) != 0UL && (wm & RightLockWhiteVictim) != 0UL)
            blackScore += 40;

        if ((wm & TriangleWhiteMask) == TriangleWhiteMask)
            whiteScore += 20;
        if ((bm & TriangleBlackMask) == TriangleBlackMask)
            blackScore += 20;

        if ((wm & OreoWhiteMask) == OreoWhiteMask)
            whiteScore += 20;
        if ((bm & OreoBlackMask) == OreoBlackMask)
            blackScore += 20;

        if ((wm & BridgeWhiteMask) == BridgeWhiteMask)
            whiteScore += 30;
        if ((bm & BridgeBlackMask) == BridgeBlackMask)
            blackScore += 30;

        if ((wm & DogWhiteMan) != 0UL && (bm & DogBlackVictim) != 0UL)
            whiteScore += 10;
        if ((bm & DogBlackMan) != 0UL && (wm & DogWhiteVictim) != 0UL)
            blackScore += 10;

        int netScore = whiteScore - blackScore;
        return pos.SideToMove == PieceColor.White ? netScore : -netScore;
    }

    /// <summary>
    /// International Draughts (Flying Kings + Backward Man Captures) evaluation combining the baseline
    /// continuous advancement, center control, and full back-rank defense with <c>board_eval.c</c>'s
    /// Runaway Checkers (<c>ConeWhite</c>/<c>ConeBlack</c>), <c>tail_pins</c>, back-rank structural formations
    /// (<c>Bridge</c>, <c>Triangle</c>, <c>Oreo</c>), Flying King main-diagonal / short-corner PSTs, and
    /// material-gated simplification bonus.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EvaluateInternational(in BitPosition pos)
    {
        ulong wm = pos.WhiteMen;
        ulong wk = pos.WhiteKings;
        ulong bm = pos.BlackMen;
        ulong bk = pos.BlackKings;
        ulong white = wm | wk;
        ulong black = bm | bk;
        ulong allPieces = white | black;

        int wmCount = BitOperations.PopCount(wm);
        int wkCount = BitOperations.PopCount(wk);
        int bmCount = BitOperations.PopCount(bm);
        int bkCount = BitOperations.PopCount(bk);

        int whiteCount = wmCount + wkCount;
        int blackCount = bmCount + bkCount;
        int totalPieces = whiteCount + blackCount;

        // 1. Base Material (Man = 100, Flying King = 300)
        int whiteMaterial = (wmCount * ManValue) + (wkCount * InternationalKingValue);
        int blackMaterial = (bmCount * ManValue) + (bkCount * InternationalKingValue);
        int whiteScore = whiteMaterial;
        int blackScore = blackMaterial;

        // 2. Continuous Man Advancement (critical in International Draughts where crowning a 300-cp Flying King is +200 cp)
        // Plus extra +1 cp/rank endgame push when totalPieces <= 12
        int whiteAdvance =
              BitOperations.PopCount(wm & Row6) * 1
            + BitOperations.PopCount(wm & Row5) * 2
            + BitOperations.PopCount(wm & Row4) * 3
            + BitOperations.PopCount(wm & Row3) * 4
            + BitOperations.PopCount(wm & Row2) * 5
            + BitOperations.PopCount(wm & Row1) * 6
            + BitOperations.PopCount(wm & Row0) * 7;

        int blackAdvance =
              BitOperations.PopCount(bm & Row1) * 1
            + BitOperations.PopCount(bm & Row2) * 2
            + BitOperations.PopCount(bm & Row3) * 3
            + BitOperations.PopCount(bm & Row4) * 4
            + BitOperations.PopCount(bm & Row5) * 5
            + BitOperations.PopCount(bm & Row6) * 6
            + BitOperations.PopCount(bm & Row7) * 7;

        int stepWeight = totalPieces <= 12 ? 6 : 5;
        whiteScore += whiteAdvance * stepWeight;
        blackScore += blackAdvance * stepWeight;

        // 3. Center Control (+12 cp) + Back-Rank Defense (+15 cp) + Man PST nuances (+2/+4/+6 cp)
        whiteScore += (BitOperations.PopCount(white & CenterMask) * CenterControlBonus)
                    + (BitOperations.PopCount(wm & Row7) * BackRankDefenseBonus)
                    + (BitOperations.PopCount(wm & WhiteManPst1Mask) * 2)
                    + (BitOperations.PopCount(wm & WhiteManPst2Mask) * 4)
                    + (BitOperations.PopCount(wm & WhiteManPst3Mask) * 6);

        blackScore += (BitOperations.PopCount(black & CenterMask) * CenterControlBonus)
                    + (BitOperations.PopCount(bm & Row0) * BackRankDefenseBonus)
                    + (BitOperations.PopCount(bm & BlackManPst1Mask) * 2)
                    + (BitOperations.PopCount(bm & BlackManPst2Mask) * 4)
                    + (BitOperations.PopCount(bm & BlackManPst3Mask) * 6);

        // 4. Runaway Checkers / Unstoppable Passers (when enemy has 0 Flying Kings and forward cone is empty)
        if (bk == 0UL && wm != 0UL)
        {
            ulong candidateWm = bm == 0UL
                ? wm
                : wm & ((1UL << ((BitOperations.TrailingZeroCount(bm) & ~7) + 8)) - 1UL);

            while (candidateWm != 0UL)
            {
                int sq = BitOperations.TrailingZeroCount(candidateWm);
                candidateWm &= candidateWm - 1UL;
                if ((ConeWhite[sq] & allPieces) == 0UL)
                {
                    whiteScore += 40 + (6 * (7 - (sq >> 3)));
                }
            }
        }

        if (wk == 0UL && bm != 0UL)
        {
            ulong candidateBm = wm == 0UL
                ? bm
                : bm & ~((1UL << ((63 - BitOperations.LeadingZeroCount(wm)) & ~7)) - 1UL);

            while (candidateBm != 0UL)
            {
                int sq = BitOperations.TrailingZeroCount(candidateBm);
                candidateBm &= candidateBm - 1UL;
                if ((ConeBlack[sq] & allPieces) == 0UL)
                {
                    blackScore += 40 + (6 * (sq >> 3));
                }
            }
        }

        // 5. King Tail Pins (+10 cp) & Flying King Diagonal / Short-Corner PST
        if ((wk | bk) != 0UL)
        {
            ulong whitePins = ((wk & Cols0To5) & (bm >> 9) & (bm >> 18))
                            | ((wk & Cols2To7) & (bm >> 7) & (bm >> 14));
            ulong blackPins = ((bk & Cols2To7) & (wm << 9) & (wm << 18))
                            | ((bk & Cols0To5) & (wm << 7) & (wm << 14));

            whiteScore += BitOperations.PopCount(whitePins) * 10;
            blackScore += BitOperations.PopCount(blackPins) * 10;

            if (wk != 0UL)
            {
                whiteScore += (BitOperations.PopCount(wk & FlyingKingMainDiagonalMask) * 12)
                            + (BitOperations.PopCount(wk & FlyingKingInnerMask) * 6)
                            - (BitOperations.PopCount(wk & FlyingKingShortCornerMask) * 20);
            }
            if (bk != 0UL)
            {
                blackScore += (BitOperations.PopCount(bk & FlyingKingMainDiagonalMask) * 12)
                            + (BitOperations.PopCount(bk & FlyingKingInnerMask) * 6)
                            - (BitOperations.PopCount(bk & FlyingKingShortCornerMask) * 20);
            }
        }

        // 6. Material-Gated Simplification Bonus:
        // In Flying Kings (where 1 King = 300 cp > 2 Men = 200 cp), only award the simplification bonus
        // to the side strictly ahead in material (by >= 80 cp), scaled by the losing side's remaining pieces
        // so the winning side actively trades down opponent pieces without ever preferring 2 Men over 1 Flying King.
        if (whiteMaterial >= blackMaterial + 80)
        {
            whiteScore += CalculateInternationalTradeBonus(blackCount);
        }
        else if (blackMaterial >= whiteMaterial + 80)
        {
            blackScore += CalculateInternationalTradeBonus(whiteCount);
        }

        // 7. Back-Rank Structural Formations (Bridge, Triangle, Oreo)
        if ((wm & TriangleWhiteMask) == TriangleWhiteMask)
            whiteScore += 12;
        if ((bm & TriangleBlackMask) == TriangleBlackMask)
            blackScore += 12;

        if ((wm & OreoWhiteMask) == OreoWhiteMask)
            whiteScore += 12;
        if ((bm & OreoBlackMask) == OreoBlackMask)
            blackScore += 12;

        if ((wm & BridgeWhiteMask) == BridgeWhiteMask)
            whiteScore += 18;
        if ((bm & BridgeBlackMask) == BridgeBlackMask)
            blackScore += 18;

        int netScore = whiteScore - blackScore;
        return pos.SideToMove == PieceColor.White ? netScore : -netScore;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CalculateInternationalTradeBonus(int opponentPieces) => opponentPieces switch
    {
        0 => 400,
        1 => 120,
        2 => 90,
        3 => 65,
        4 => 45,
        5 => 30,
        6 => 20,
        _ => (12 - Math.Min(12, opponentPieces)) * 3
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CalculatePieceBonus(int numPieces) => numPieces switch
    {
        <= 2 => 400,
        3 => 300,
        4 => 200,
        5 => 120,
        6 => 60,
        _ => 20
    };

    private static ulong[] ComputeRunawayCones(PieceColor player)
    {
        var cones = new ulong[64];
        for (int pos = 0; pos < 64; pos++)
        {
            ulong mask = 0UL;
            int r = pos >> 3;
            int c = pos & 7;
            if (player == PieceColor.White)
            {
                // White men move toward row 0
                for (int k = 1; k <= r; k++)
                {
                    for (int dc = -k; dc <= k; dc += 2)
                    {
                        int cc = c + dc;
                        if ((uint)cc < 8u)
                        {
                            mask |= 1UL << (((r - k) << 3) + cc);
                        }
                    }
                }
            }
            else
            {
                // Black men move toward row 7
                for (int k = 1; k <= 7 - r; k++)
                {
                    for (int dc = -k; dc <= k; dc += 2)
                    {
                        int cc = c + dc;
                        if ((uint)cc < 8u)
                        {
                            mask |= 1UL << (((r + k) << 3) + cc);
                        }
                    }
                }
            }
            cones[pos] = mask;
        }
        return cones;
    }
}
