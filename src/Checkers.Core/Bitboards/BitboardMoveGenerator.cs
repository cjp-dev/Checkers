using System.Numerics;
using System.Runtime.CompilerServices;
using Checkers.Core.Models;

namespace Checkers.Core.Bitboards;

/// <summary>
/// High-performance 64-bit bitboard move generator supporting both
/// <see cref="CheckersVariant.International"/> (Flying Kings) and
/// <see cref="CheckersVariant.English"/> (1-Step Kings).
/// Guarantees 100% move-for-move and order-for-order equivalence with <see cref="Engine.RuleEngine"/>.
/// </summary>
public static class BitboardMoveGenerator
{
    /// <summary>
    /// Fast check whether the active player has at least one legal move (capture or quiet).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasAnyLegalMove(in BitPosition pos, CheckersVariant variant)
    {
        ulong empty = pos.Empty;
        ulong ownMen, ownKings, enemy;

        if (pos.SideToMove == PieceColor.White)
        {
            ownMen = pos.WhiteMen;
            ownKings = pos.WhiteKings;
            enemy = pos.Black;

            // Quiet 1-step moves for White men (directions 0 and 1: -9, -7)
            if (((ownMen & BitboardMasks.NotColA) >> 9 & empty) != 0UL ||
                ((ownMen & BitboardMasks.NotColH) >> 7 & empty) != 0UL)
            {
                return true;
            }

            // Capture jumps for White men
            if ((ownMen & BitboardMasks.NotColAB & (enemy << 9) & (empty << 18)) != 0UL ||
                (ownMen & BitboardMasks.NotColGH & (enemy << 7) & (empty << 14)) != 0UL)
            {
                return true;
            }
        }
        else
        {
            ownMen = pos.BlackMen;
            ownKings = pos.BlackKings;
            enemy = pos.White;

            // Quiet 1-step moves for Black men (directions 2 and 3: +7, +9)
            if (((ownMen & BitboardMasks.NotColA) << 7 & empty) != 0UL ||
                ((ownMen & BitboardMasks.NotColH) << 9 & empty) != 0UL)
            {
                return true;
            }

            // Capture jumps for Black men
            if ((ownMen & BitboardMasks.NotColAB & (enemy >> 7) & (empty >> 14)) != 0UL ||
                (ownMen & BitboardMasks.NotColGH & (enemy >> 9) & (empty >> 18)) != 0UL)
            {
                return true;
            }
        }

        if (ownKings == 0UL)
            return false;

        // Any 1-step quiet move for Kings (valid in both English and International)
        if (((ownKings & BitboardMasks.NotColA) >> 9 & empty) != 0UL ||
            ((ownKings & BitboardMasks.NotColH) >> 7 & empty) != 0UL ||
            ((ownKings & BitboardMasks.NotColA) << 7 & empty) != 0UL ||
            ((ownKings & BitboardMasks.NotColH) << 9 & empty) != 0UL)
        {
            return true;
        }

        // 1-hop King captures (valid in both English and International)
        if ((ownKings & BitboardMasks.NotColAB & (enemy << 9) & (empty << 18)) != 0UL ||
            (ownKings & BitboardMasks.NotColGH & (enemy << 7) & (empty << 14)) != 0UL ||
            (ownKings & BitboardMasks.NotColAB & (enemy >> 7) & (empty >> 14)) != 0UL ||
            (ownKings & BitboardMasks.NotColGH & (enemy >> 9) & (empty >> 18)) != 0UL)
        {
            return true;
        }

        if (variant == CheckersVariant.English)
            return false;

        // International Flying Kings: check long-range captures when adjacent squares are blocked by enemy
        ulong occupied = pos.Occupied;
        ulong kings = ownKings;
        while (kings != 0UL)
        {
            int sq = BitOperations.TrailingZeroCount(kings);
            kings &= kings - 1;
            if (HasFlyingKingCaptureFrom(sq, sq, occupied, enemy, 0UL))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Generates all legal <see cref="BitMove"/>s into the caller-supplied span without heap allocations.
    /// Returns the number of moves written to <paramref name="buffer"/>.
    /// </summary>
    public static int Generate(in BitPosition pos, CheckersVariant variant, Span<BitMove> buffer)
    {
        int count = GenerateCaptures(in pos, variant, buffer);
        if (count > 0)
            return count;

        return GenerateQuietMoves(in pos, variant, buffer);
    }

    /// <summary>
    /// Generates only legal capture moves into <paramref name="buffer"/> (used by Quiescence search and main search).
    /// Returns 0 if no captures are available.
    /// </summary>
    public static int GenerateCaptures(in BitPosition pos, CheckersVariant variant, Span<BitMove> buffer)
    {
        ulong empty = pos.Empty;
        ulong ownMen, ownKings, enemy;

        if (pos.SideToMove == PieceColor.White)
        {
            ownMen = pos.WhiteMen;
            ownKings = pos.WhiteKings;
            enemy = pos.Black;
        }
        else
        {
            ownMen = pos.BlackMen;
            ownKings = pos.BlackKings;
            enemy = pos.White;
        }

        if (enemy == 0UL)
            return 0;

        // Compute bitmask of men that can make an initial jump
        ulong manJumpers = pos.SideToMove == PieceColor.White
            ? (ownMen & (
                (BitboardMasks.NotColAB & (enemy << 9) & (empty << 18)) |
                (BitboardMasks.NotColGH & (enemy << 7) & (empty << 14))))
            : (ownMen & (
                (BitboardMasks.NotColAB & (enemy >> 7) & (empty >> 14)) |
                (BitboardMasks.NotColGH & (enemy >> 9) & (empty >> 18))));

        ulong candidates;
        if (ownKings == 0UL)
        {
            candidates = manJumpers;
        }
        else if (variant == CheckersVariant.English)
        {
            ulong kingJumpers = ownKings & (
                (BitboardMasks.NotColAB & (enemy << 9) & (empty << 18)) |
                (BitboardMasks.NotColGH & (enemy << 7) & (empty << 14)) |
                (BitboardMasks.NotColAB & (enemy >> 7) & (empty >> 14)) |
                (BitboardMasks.NotColGH & (enemy >> 9) & (empty >> 18)));
            candidates = manJumpers | kingJumpers;
        }
        else
        {
            candidates = manJumpers | ownKings;
        }

        if (candidates == 0UL)
            return 0;

        ulong occupied = pos.Occupied;
        int count = 0;

        while (candidates != 0UL)
        {
            int sq = BitOperations.TrailingZeroCount(candidates);
            candidates &= candidates - 1;
            ulong sqBit = 1UL << sq;

            if ((ownMen & sqBit) != 0UL)
            {
                FindManCapturesBit(
                    pos.SideToMove,
                    initialFrom: (byte)sq,
                    currentSq: sq,
                    occupiedExceptInitial: occupied ^ sqBit,
                    enemy: enemy,
                    capturedSoFar: 0UL,
                    buffer,
                    ref count);
            }
            else if (variant == CheckersVariant.English)
            {
                FindEnglishKingCapturesBit(
                    initialFrom: (byte)sq,
                    currentSq: sq,
                    occupiedExceptInitial: occupied ^ sqBit,
                    enemy: enemy,
                    capturedSoFar: 0UL,
                    buffer,
                    ref count);
            }
            else
            {
                FindFlyingKingCapturesBit(
                    initialFrom: (byte)sq,
                    currentSq: sq,
                    occupied: occupied,
                    occupiedExceptInitial: occupied ^ sqBit,
                    enemy: enemy,
                    capturedSoFar: 0UL,
                    buffer,
                    ref count);
            }
        }

        return count;
    }

    /// <summary>
    /// Generates all quiet moves into <paramref name="buffer"/> (assuming no captures exist).
    /// </summary>
    public static int GenerateQuietMoves(in BitPosition pos, CheckersVariant variant, Span<BitMove> buffer)
    {
        ulong empty = pos.Empty;
        ulong occupied = pos.Occupied;
        ulong ownMen = pos.SideToMove == PieceColor.White ? pos.WhiteMen : pos.BlackMen;
        ulong own = pos.SideToMove == PieceColor.White ? pos.White : pos.Black;
        int startDir = pos.SideToMove == PieceColor.White ? 0 : 2;
        int endDir = pos.SideToMove == PieceColor.White ? 2 : 4;
        int count = 0;

        while (own != 0UL)
        {
            int sq = BitOperations.TrailingZeroCount(own);
            own &= own - 1;
            ulong sqBit = 1UL << sq;

            if ((ownMen & sqBit) != 0UL)
            {
                for (int d = startDir; d < endDir; d++)
                {
                    sbyte dest = BitboardMasks.StepTarget[d, sq];
                    if (dest >= 0 && (empty & (1UL << dest)) != 0UL)
                    {
                        bool isPromo = pos.SideToMove == PieceColor.White ? dest < 8 : dest >= 56;
                        buffer[count++] = new BitMove((byte)sq, (byte)dest, 0UL, isPromo);
                    }
                }
            }
            else if (variant == CheckersVariant.English)
            {
                for (int d = 0; d < 4; d++)
                {
                    sbyte dest = BitboardMasks.StepTarget[d, sq];
                    if (dest >= 0 && (empty & (1UL << dest)) != 0UL)
                    {
                        buffer[count++] = new BitMove((byte)sq, (byte)dest, 0UL, false);
                    }
                }
            }
            else
            {
                for (int d = 0; d < 4; d++)
                {
                    ulong ray = BitboardMasks.Rays[d, sq];
                    ulong blockers = ray & occupied;
                    if (d < 2)
                    {
                        ulong emptyBefore = blockers != 0UL
                            ? ray & ~((1UL << (64 - BitOperations.LeadingZeroCount(blockers))) - 1UL)
                            : ray;
                        while (emptyBefore != 0UL)
                        {
                            int dest = 63 - BitOperations.LeadingZeroCount(emptyBefore);
                            emptyBefore ^= 1UL << dest;
                            buffer[count++] = new BitMove((byte)sq, (byte)dest, 0UL, false);
                        }
                    }
                    else
                    {
                        ulong emptyBefore = blockers != 0UL
                            ? ray & ((1UL << BitOperations.TrailingZeroCount(blockers)) - 1UL)
                            : ray;
                        while (emptyBefore != 0UL)
                        {
                            int dest = BitOperations.TrailingZeroCount(emptyBefore);
                            emptyBefore &= emptyBefore - 1;
                            buffer[count++] = new BitMove((byte)sq, (byte)dest, 0UL, false);
                        }
                    }
                }
            }
        }

        return count;
    }

    #region BitMove Capture Recursion

    private static void FindManCapturesBit(
        PieceColor side,
        byte initialFrom,
        int currentSq,
        ulong occupiedExceptInitial,
        ulong enemy,
        ulong capturedSoFar,
        Span<BitMove> buffer,
        ref int count)
    {
        int startDir = side == PieceColor.White ? 0 : 2;
        int endDir = side == PieceColor.White ? 2 : 4;
        ulong unjumpedEnemy = enemy & ~capturedSoFar;

        for (int d = startDir; d < endDir; d++)
        {
            sbyte jumpedSq = BitboardMasks.JumpMiddle[d, currentSq];
            sbyte landingSq = BitboardMasks.JumpLanding[d, currentSq];
            if (landingSq < 0)
                continue;

            ulong landBit = 1UL << landingSq;
            if ((occupiedExceptInitial & landBit) != 0UL)
                continue;

            ulong jumpBit = 1UL << jumpedSq;
            if ((unjumpedEnemy & jumpBit) == 0UL)
                continue;

            ulong nextCaptured = capturedSoFar | jumpBit;
            bool isCrown = side == PieceColor.White ? landingSq < 8 : landingSq >= 56;

            if (isCrown)
            {
                buffer[count++] = new BitMove(initialFrom, (byte)landingSq, nextCaptured, isPromotion: true);
            }
            else
            {
                int countBefore = count;
                FindManCapturesBit(side, initialFrom, landingSq, occupiedExceptInitial, enemy, nextCaptured, buffer, ref count);
                if (count == countBefore)
                {
                    buffer[count++] = new BitMove(initialFrom, (byte)landingSq, nextCaptured, isPromotion: false);
                }
            }
        }
    }

    private static void FindEnglishKingCapturesBit(
        byte initialFrom,
        int currentSq,
        ulong occupiedExceptInitial,
        ulong enemy,
        ulong capturedSoFar,
        Span<BitMove> buffer,
        ref int count)
    {
        ulong unjumpedEnemy = enemy & ~capturedSoFar;

        for (int d = 0; d < 4; d++)
        {
            sbyte jumpedSq = BitboardMasks.JumpMiddle[d, currentSq];
            sbyte landingSq = BitboardMasks.JumpLanding[d, currentSq];
            if (landingSq < 0)
                continue;

            ulong landBit = 1UL << landingSq;
            if ((occupiedExceptInitial & landBit) != 0UL)
                continue;

            ulong jumpBit = 1UL << jumpedSq;
            if ((unjumpedEnemy & jumpBit) == 0UL)
                continue;

            ulong nextCaptured = capturedSoFar | jumpBit;
            int countBefore = count;
            FindEnglishKingCapturesBit(initialFrom, landingSq, occupiedExceptInitial, enemy, nextCaptured, buffer, ref count);
            if (count == countBefore)
            {
                buffer[count++] = new BitMove(initialFrom, (byte)landingSq, nextCaptured, isPromotion: false);
            }
        }
    }

    private static void FindFlyingKingCapturesBit(
        byte initialFrom,
        int currentSq,
        ulong occupied,
        ulong occupiedExceptInitial,
        ulong enemy,
        ulong capturedSoFar,
        Span<BitMove> buffer,
        ref int count)
    {
        ulong unjumpedEnemy = enemy & ~capturedSoFar;

        for (int d = 0; d < 4; d++)
        {
            ulong ray = BitboardMasks.Rays[d, currentSq];
            ulong blockers = ray & occupied;
            if (blockers == 0UL)
                continue;

            int firstBlockerSq = d < 2
                ? 63 - BitOperations.LeadingZeroCount(blockers)
                : BitOperations.TrailingZeroCount(blockers);

            ulong jumpBit = 1UL << firstBlockerSq;
            if ((unjumpedEnemy & jumpBit) == 0UL)
                continue;

            ulong rayBeyond = BitboardMasks.Rays[d, firstBlockerSq];
            ulong blockersBeyond = rayBeyond & occupiedExceptInitial;
            ulong landingMask;

            if (d < 2)
            {
                landingMask = blockersBeyond != 0UL
                    ? rayBeyond & ~((1UL << (64 - BitOperations.LeadingZeroCount(blockersBeyond))) - 1UL)
                    : rayBeyond;
            }
            else
            {
                landingMask = blockersBeyond != 0UL
                    ? rayBeyond & ((1UL << BitOperations.TrailingZeroCount(blockersBeyond)) - 1UL)
                    : rayBeyond;
            }

            if (landingMask == 0UL)
                continue;

            ulong nextCaptured = capturedSoFar | jumpBit;
            int countBeforeRay = count;

            ulong iterMask = landingMask;
            while (iterMask != 0UL)
            {
                int landingSq;
                if (d < 2)
                {
                    landingSq = 63 - BitOperations.LeadingZeroCount(iterMask);
                    iterMask ^= 1UL << landingSq;
                }
                else
                {
                    landingSq = BitOperations.TrailingZeroCount(iterMask);
                    iterMask &= iterMask - 1;
                }

                FindFlyingKingCapturesBit(
                    initialFrom,
                    landingSq,
                    occupied,
                    occupiedExceptInitial,
                    enemy,
                    nextCaptured,
                    buffer,
                    ref count);
            }

            // If no landing square along this ray had a continuation jump, all landing squares are valid terminal stops
            if (count == countBeforeRay)
            {
                iterMask = landingMask;
                while (iterMask != 0UL)
                {
                    int landingSq;
                    if (d < 2)
                    {
                        landingSq = 63 - BitOperations.LeadingZeroCount(iterMask);
                        iterMask ^= 1UL << landingSq;
                    }
                    else
                    {
                        landingSq = BitOperations.TrailingZeroCount(iterMask);
                        iterMask &= iterMask - 1;
                    }

                    buffer[count++] = new BitMove(initialFrom, (byte)landingSq, nextCaptured, isPromotion: false);
                }
            }
        }
    }

    private static bool HasFlyingKingCaptureFrom(
        int initialFrom,
        int currentSq,
        ulong occupied,
        ulong enemy,
        ulong capturedSoFar)
    {
        ulong unjumpedEnemy = enemy & ~capturedSoFar;
        ulong occupiedExceptInitial = occupied & ~(1UL << initialFrom);

        for (int d = 0; d < 4; d++)
        {
            ulong ray = BitboardMasks.Rays[d, currentSq];
            ulong blockers = ray & occupied;
            if (blockers == 0UL)
                continue;

            int firstBlockerSq = d < 2
                ? 63 - BitOperations.LeadingZeroCount(blockers)
                : BitOperations.TrailingZeroCount(blockers);

            if ((unjumpedEnemy & (1UL << firstBlockerSq)) == 0UL)
                continue;

            ulong rayBeyond = BitboardMasks.Rays[d, firstBlockerSq];
            if (rayBeyond == 0UL)
                continue;

            int nextSq = d < 2
                ? 63 - BitOperations.LeadingZeroCount(rayBeyond)
                : BitOperations.TrailingZeroCount(rayBeyond);

            if ((occupiedExceptInitial & (1UL << nextSq)) == 0UL)
                return true;
        }

        return false;
    }

    #endregion

    #region Full Move (with Path & Notation) Generation for IRuleEngine / UI

    /// <summary>
    /// Generates full <see cref="Move"/> objects (including <see cref="Move.Path"/> and <see cref="Move.Notation"/>)
    /// in the exact same order as <see cref="Engine.RuleEngine.GetLegalMoves"/>.
    /// </summary>
    public static IReadOnlyList<Move> GenerateMoves(in BitPosition pos, CheckersVariant variant)
    {
        var captureMoves = new List<Move>();
        ulong occupied = pos.Occupied;
        ulong ownMen = pos.SideToMove == PieceColor.White ? pos.WhiteMen : pos.BlackMen;
        ulong ownKings = pos.SideToMove == PieceColor.White ? pos.WhiteKings : pos.BlackKings;
        ulong enemy = pos.SideToMove == PieceColor.White ? pos.Black : pos.White;
        ulong empty = pos.Empty;

        if (enemy != 0UL)
        {
            ulong manJumpers = pos.SideToMove == PieceColor.White
                ? (ownMen & (
                    (BitboardMasks.NotColAB & (enemy << 9) & (empty << 18)) |
                    (BitboardMasks.NotColGH & (enemy << 7) & (empty << 14))))
                : (ownMen & (
                    (BitboardMasks.NotColAB & (enemy >> 7) & (empty >> 14)) |
                    (BitboardMasks.NotColGH & (enemy >> 9) & (empty >> 18))));

            ulong candidates = ownKings == 0UL
                ? manJumpers
                : variant == CheckersVariant.English
                    ? manJumpers | (ownKings & (
                        (BitboardMasks.NotColAB & (enemy << 9) & (empty << 18)) |
                        (BitboardMasks.NotColGH & (enemy << 7) & (empty << 14)) |
                        (BitboardMasks.NotColAB & (enemy >> 7) & (empty >> 14)) |
                        (BitboardMasks.NotColGH & (enemy >> 9) & (empty >> 18))))
                    : manJumpers | ownKings;

            while (candidates != 0UL)
            {
                int sq = BitOperations.TrailingZeroCount(candidates);
                candidates &= candidates - 1;
                ulong sqBit = 1UL << sq;
                var startPos = BitboardMasks.Positions[sq];

                if ((ownMen & sqBit) != 0UL)
                {
                    FindManCapturesFull(
                        pos.SideToMove,
                        startPos,
                        sq,
                        occupied ^ sqBit,
                        enemy,
                        0UL,
                        [startPos],
                        [],
                        captureMoves);
                }
                else if (variant == CheckersVariant.English)
                {
                    FindEnglishKingCapturesFull(
                        startPos,
                        sq,
                        occupied ^ sqBit,
                        enemy,
                        0UL,
                        [startPos],
                        [],
                        captureMoves);
                }
                else
                {
                    FindFlyingKingCapturesFull(
                        startPos,
                        sq,
                        occupied,
                        occupied ^ sqBit,
                        enemy,
                        0UL,
                        [startPos],
                        [],
                        captureMoves);
                }
            }
        }

        if (captureMoves.Count > 0)
            return captureMoves;

        Span<BitMove> quietBuffer = stackalloc BitMove[128];
        int quietCount = GenerateQuietMoves(in pos, variant, quietBuffer);
        var quietMoves = new List<Move>(quietCount);

        for (int i = 0; i < quietCount; i++)
        {
            var bm = quietBuffer[i];
            quietMoves.Add(Move.CreateQuiet(
                BitboardMasks.Positions[bm.From],
                BitboardMasks.Positions[bm.To],
                bm.IsPromotion));
        }

        return quietMoves;
    }

    private static void FindManCapturesFull(
        PieceColor side,
        Position initialFrom,
        int currentSq,
        ulong occupiedExceptInitial,
        ulong enemy,
        ulong capturedSoFar,
        List<Position> pathSoFar,
        List<Position> capturedList,
        List<Move> resultMoves)
    {
        int startDir = side == PieceColor.White ? 0 : 2;
        int endDir = side == PieceColor.White ? 2 : 4;
        ulong unjumpedEnemy = enemy & ~capturedSoFar;

        for (int d = startDir; d < endDir; d++)
        {
            sbyte jumpedSq = BitboardMasks.JumpMiddle[d, currentSq];
            sbyte landingSq = BitboardMasks.JumpLanding[d, currentSq];
            if (landingSq < 0)
                continue;

            if ((occupiedExceptInitial & (1UL << landingSq)) != 0UL)
                continue;

            ulong jumpBit = 1UL << jumpedSq;
            if ((unjumpedEnemy & jumpBit) == 0UL)
                continue;

            var landingPos = BitboardMasks.Positions[landingSq];
            var jumpedPos = BitboardMasks.Positions[jumpedSq];
            var newPath = new List<Position>(pathSoFar) { landingPos };
            var newCaptured = new List<Position>(capturedList) { jumpedPos };
            ulong nextCapturedMask = capturedSoFar | jumpBit;

            bool isCrown = side == PieceColor.White ? landingSq < 8 : landingSq >= 56;
            if (isCrown)
            {
                resultMoves.Add(Move.CreateCapture(initialFrom, landingPos, newPath, newCaptured, isPromotion: true));
            }
            else
            {
                int countBefore = resultMoves.Count;
                FindManCapturesFull(side, initialFrom, landingSq, occupiedExceptInitial, enemy, nextCapturedMask, newPath, newCaptured, resultMoves);
                if (resultMoves.Count == countBefore)
                {
                    resultMoves.Add(Move.CreateCapture(initialFrom, landingPos, newPath, newCaptured, isPromotion: false));
                }
            }
        }
    }

    private static void FindEnglishKingCapturesFull(
        Position initialFrom,
        int currentSq,
        ulong occupiedExceptInitial,
        ulong enemy,
        ulong capturedSoFar,
        List<Position> pathSoFar,
        List<Position> capturedList,
        List<Move> resultMoves)
    {
        ulong unjumpedEnemy = enemy & ~capturedSoFar;

        for (int d = 0; d < 4; d++)
        {
            sbyte jumpedSq = BitboardMasks.JumpMiddle[d, currentSq];
            sbyte landingSq = BitboardMasks.JumpLanding[d, currentSq];
            if (landingSq < 0)
                continue;

            if ((occupiedExceptInitial & (1UL << landingSq)) != 0UL)
                continue;

            ulong jumpBit = 1UL << jumpedSq;
            if ((unjumpedEnemy & jumpBit) == 0UL)
                continue;

            var landingPos = BitboardMasks.Positions[landingSq];
            var jumpedPos = BitboardMasks.Positions[jumpedSq];
            var newPath = new List<Position>(pathSoFar) { landingPos };
            var newCaptured = new List<Position>(capturedList) { jumpedPos };
            ulong nextCapturedMask = capturedSoFar | jumpBit;

            int countBefore = resultMoves.Count;
            FindEnglishKingCapturesFull(initialFrom, landingSq, occupiedExceptInitial, enemy, nextCapturedMask, newPath, newCaptured, resultMoves);
            if (resultMoves.Count == countBefore)
            {
                resultMoves.Add(Move.CreateCapture(initialFrom, landingPos, newPath, newCaptured, isPromotion: false));
            }
        }
    }

    private static void FindFlyingKingCapturesFull(
        Position initialFrom,
        int currentSq,
        ulong occupied,
        ulong occupiedExceptInitial,
        ulong enemy,
        ulong capturedSoFar,
        List<Position> pathSoFar,
        List<Position> capturedList,
        List<Move> resultMoves)
    {
        ulong unjumpedEnemy = enemy & ~capturedSoFar;

        for (int d = 0; d < 4; d++)
        {
            ulong ray = BitboardMasks.Rays[d, currentSq];
            ulong blockers = ray & occupied;
            if (blockers == 0UL)
                continue;

            int firstBlockerSq = d < 2
                ? 63 - BitOperations.LeadingZeroCount(blockers)
                : BitOperations.TrailingZeroCount(blockers);

            ulong jumpBit = 1UL << firstBlockerSq;
            if ((unjumpedEnemy & jumpBit) == 0UL)
                continue;

            ulong rayBeyond = BitboardMasks.Rays[d, firstBlockerSq];
            ulong blockersBeyond = rayBeyond & occupiedExceptInitial;
            ulong landingMask;

            if (d < 2)
            {
                landingMask = blockersBeyond != 0UL
                    ? rayBeyond & ~((1UL << (64 - BitOperations.LeadingZeroCount(blockersBeyond))) - 1UL)
                    : rayBeyond;
            }
            else
            {
                landingMask = blockersBeyond != 0UL
                    ? rayBeyond & ((1UL << BitOperations.TrailingZeroCount(blockersBeyond)) - 1UL)
                    : rayBeyond;
            }

            if (landingMask == 0UL)
                continue;

            var jumpedPos = BitboardMasks.Positions[firstBlockerSq];
            ulong nextCapturedMask = capturedSoFar | jumpBit;
            int countBeforeRay = resultMoves.Count;

            ulong iterMask = landingMask;
            while (iterMask != 0UL)
            {
                int landingSq;
                if (d < 2)
                {
                    landingSq = 63 - BitOperations.LeadingZeroCount(iterMask);
                    iterMask ^= 1UL << landingSq;
                }
                else
                {
                    landingSq = BitOperations.TrailingZeroCount(iterMask);
                    iterMask &= iterMask - 1;
                }

                var landingPos = BitboardMasks.Positions[landingSq];
                var newPath = new List<Position>(pathSoFar) { landingPos };
                var newCaptured = new List<Position>(capturedList) { jumpedPos };

                FindFlyingKingCapturesFull(
                    initialFrom,
                    landingSq,
                    occupied,
                    occupiedExceptInitial,
                    enemy,
                    nextCapturedMask,
                    newPath,
                    newCaptured,
                    resultMoves);
            }

            if (resultMoves.Count == countBeforeRay)
            {
                iterMask = landingMask;
                while (iterMask != 0UL)
                {
                    int landingSq;
                    if (d < 2)
                    {
                        landingSq = 63 - BitOperations.LeadingZeroCount(iterMask);
                        iterMask ^= 1UL << landingSq;
                    }
                    else
                    {
                        landingSq = BitOperations.TrailingZeroCount(iterMask);
                        iterMask &= iterMask - 1;
                    }

                    var landingPos = BitboardMasks.Positions[landingSq];
                    var newPath = new List<Position>(pathSoFar) { landingPos };
                    var newCaptured = new List<Position>(capturedList) { jumpedPos };

                    resultMoves.Add(Move.CreateCapture(initialFrom, landingPos, newPath, newCaptured, isPromotion: false));
                }
            }
        }
    }

    #endregion
}
