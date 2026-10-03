using System.Numerics;
using System.Runtime.CompilerServices;
using Checkers.Core.Engine;
using Checkers.Core.Models;

namespace Checkers.Core.Bitboards;

/// <summary>
/// Value-type 64-bit bitboard representation of a Checkers position (4 x ulong piece bitboards + Zobrist hash + clock + side).
/// Supports zero-allocation copy-make transitions with incremental Zobrist hashing.
/// </summary>
public struct BitPosition
{
    public const ulong InitialBlackMen = 0x0000000000AA55AAUL;
    public const ulong InitialWhiteMen = 0x55AA550000000000UL;

    public ulong WhiteMen;
    public ulong BlackMen;
    public ulong WhiteKings;
    public ulong BlackKings;
    public ulong Hash;
    public int HalfMoveClock;
    public PieceColor SideToMove;

    public readonly ulong White
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => WhiteMen | WhiteKings;
    }

    public readonly ulong Black
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BlackMen | BlackKings;
    }

    public readonly ulong Kings
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => WhiteKings | BlackKings;
    }

    public readonly ulong Occupied
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => WhiteMen | BlackMen | WhiteKings | BlackKings;
    }

    public readonly ulong Empty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BitboardMasks.DarkSquares & ~(WhiteMen | BlackMen | WhiteKings | BlackKings);
    }

    public readonly int WhitePiecesCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BitOperations.PopCount(WhiteMen | WhiteKings);
    }

    public readonly int BlackPiecesCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BitOperations.PopCount(BlackMen | BlackKings);
    }

    public readonly int WhiteKingsCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BitOperations.PopCount(WhiteKings);
    }

    public readonly int BlackKingsCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BitOperations.PopCount(BlackKings);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Piece? GetPiece(int row, int col)
    {
        if ((uint)row >= 8u || (uint)col >= 8u)
            return null;

        ulong bit = 1UL << ((row << 3) + col);
        if ((Occupied & bit) == 0UL)
            return null;

        if ((WhiteMen & bit) != 0UL)
            return Piece.WhiteMan;
        if ((BlackMen & bit) != 0UL)
            return Piece.BlackMan;
        if ((WhiteKings & bit) != 0UL)
            return Piece.WhiteKing;
        return Piece.BlackKing;
    }

    public void SetPiece(int row, int col, Piece? piece)
    {
        ulong clearMask = ~(1UL << ((row << 3) + col));
        WhiteMen &= clearMask;
        BlackMen &= clearMask;
        WhiteKings &= clearMask;
        BlackKings &= clearMask;

        if (piece.HasValue)
        {
            ulong bit = ~clearMask;
            if (piece.Value.Color == PieceColor.White)
            {
                if (piece.Value.IsKing)
                    WhiteKings |= bit;
                else
                    WhiteMen |= bit;
            }
            else
            {
                if (piece.Value.IsKing)
                    BlackKings |= bit;
                else
                    BlackMen |= bit;
            }
        }
    }

    public ulong RecalculateHash()
    {
        ulong hash = Zobrist.GetTurnKey(SideToMove);

        ulong wm = WhiteMen;
        while (wm != 0UL)
        {
            int sq = BitOperations.TrailingZeroCount(wm);
            wm &= wm - 1;
            hash ^= Zobrist.GetPieceKey(sq, Zobrist.WhiteManIndex);
        }

        ulong wk = WhiteKings;
        while (wk != 0UL)
        {
            int sq = BitOperations.TrailingZeroCount(wk);
            wk &= wk - 1;
            hash ^= Zobrist.GetPieceKey(sq, Zobrist.WhiteKingIndex);
        }

        ulong bm = BlackMen;
        while (bm != 0UL)
        {
            int sq = BitOperations.TrailingZeroCount(bm);
            bm &= bm - 1;
            hash ^= Zobrist.GetPieceKey(sq, Zobrist.BlackManIndex);
        }

        ulong bk = BlackKings;
        while (bk != 0UL)
        {
            int sq = BitOperations.TrailingZeroCount(bk);
            bk &= bk - 1;
            hash ^= Zobrist.GetPieceKey(sq, Zobrist.BlackKingIndex);
        }

        Hash = hash;
        return hash;
    }

    public static BitPosition CreateEmpty(PieceColor activePlayer = PieceColor.White)
    {
        var pos = new BitPosition
        {
            SideToMove = activePlayer
        };
        pos.RecalculateHash();
        return pos;
    }

    public static BitPosition CreateInitial()
    {
        var pos = new BitPosition
        {
            WhiteMen = InitialWhiteMen,
            BlackMen = InitialBlackMen,
            WhiteKings = 0UL,
            BlackKings = 0UL,
            HalfMoveClock = 0,
            SideToMove = PieceColor.White
        };
        pos.RecalculateHash();
        return pos;
    }

    /// <summary>
    /// Extracts the underlying <see cref="BitPosition"/> from a <see cref="BoardState"/> in O(1) time.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BitPosition FromBoardState(BoardState state) => state.BitPosition;

    /// <summary>
    /// Wraps this <see cref="BitPosition"/> in a <see cref="BoardState"/> snapshot in O(1) time.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly BoardState ToBoardState(int fullMoveNumber) => new(this, fullMoveNumber);

    /// <summary>
    /// Applies a <see cref="BitMove"/> using copy-make and incremental Zobrist hash updates.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly BitPosition Apply(in BitMove move)
    {
        BitPosition next = this;
        int from = move.From;
        int to = move.To;
        ulong fromBit = 1UL << from;
        ulong toBit = 1UL << to;
        ulong hash = next.Hash;

        if (SideToMove == PieceColor.White)
        {
            if ((next.WhiteMen & fromBit) != 0)
            {
                next.WhiteMen ^= fromBit;
                hash ^= Zobrist.GetPieceKey(from, Zobrist.WhiteManIndex);

                if (move.IsPromotion)
                {
                    next.WhiteKings |= toBit;
                    hash ^= Zobrist.GetPieceKey(to, Zobrist.WhiteKingIndex);
                }
                else
                {
                    next.WhiteMen |= toBit;
                    hash ^= Zobrist.GetPieceKey(to, Zobrist.WhiteManIndex);
                }
            }
            else
            {
                next.WhiteKings ^= fromBit;
                hash ^= Zobrist.GetPieceKey(from, Zobrist.WhiteKingIndex);
                next.WhiteKings |= toBit;
                hash ^= Zobrist.GetPieceKey(to, Zobrist.WhiteKingIndex);
            }

            ulong captured = move.Captured;
            if (captured != 0UL)
            {
                ulong capMen = next.BlackMen & captured;
                next.BlackMen ^= capMen;
                while (capMen != 0UL)
                {
                    int sq = BitOperations.TrailingZeroCount(capMen);
                    capMen &= capMen - 1;
                    hash ^= Zobrist.GetPieceKey(sq, Zobrist.BlackManIndex);
                }

                ulong capKings = next.BlackKings & captured;
                next.BlackKings ^= capKings;
                while (capKings != 0UL)
                {
                    int sq = BitOperations.TrailingZeroCount(capKings);
                    capKings &= capKings - 1;
                    hash ^= Zobrist.GetPieceKey(sq, Zobrist.BlackKingIndex);
                }
            }

            next.SideToMove = PieceColor.Black;
        }
        else
        {
            if ((next.BlackMen & fromBit) != 0)
            {
                next.BlackMen ^= fromBit;
                hash ^= Zobrist.GetPieceKey(from, Zobrist.BlackManIndex);

                if (move.IsPromotion)
                {
                    next.BlackKings |= toBit;
                    hash ^= Zobrist.GetPieceKey(to, Zobrist.BlackKingIndex);
                }
                else
                {
                    next.BlackMen |= toBit;
                    hash ^= Zobrist.GetPieceKey(to, Zobrist.BlackManIndex);
                }
            }
            else
            {
                next.BlackKings ^= fromBit;
                hash ^= Zobrist.GetPieceKey(from, Zobrist.BlackKingIndex);
                next.BlackKings |= toBit;
                hash ^= Zobrist.GetPieceKey(to, Zobrist.BlackKingIndex);
            }

            ulong captured = move.Captured;
            if (captured != 0UL)
            {
                ulong capMen = next.WhiteMen & captured;
                next.WhiteMen ^= capMen;
                while (capMen != 0UL)
                {
                    int sq = BitOperations.TrailingZeroCount(capMen);
                    capMen &= capMen - 1;
                    hash ^= Zobrist.GetPieceKey(sq, Zobrist.WhiteManIndex);
                }

                ulong capKings = next.WhiteKings & captured;
                next.WhiteKings ^= capKings;
                while (capKings != 0UL)
                {
                    int sq = BitOperations.TrailingZeroCount(capKings);
                    capKings &= capKings - 1;
                    hash ^= Zobrist.GetPieceKey(sq, Zobrist.WhiteKingIndex);
                }
            }

            next.SideToMove = PieceColor.White;
        }

        if (move.IsCapture || move.IsPromotion)
        {
            next.HalfMoveClock = 0;
        }
        else
        {
            next.HalfMoveClock++;
        }

        hash ^= Zobrist.TurnToggleKey;
        next.Hash = hash;
        return next;
    }
}
