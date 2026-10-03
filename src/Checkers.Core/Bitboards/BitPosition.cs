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

    /// <summary>
    /// Converts a <see cref="BoardState"/> into a <see cref="BitPosition"/>.
    /// </summary>
    public static BitPosition FromBoardState(BoardState state)
    {
        ulong wm = 0UL, bm = 0UL, wk = 0UL, bk = 0UL;

        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                var piece = state.GetPiece(r, c);
                if (!piece.HasValue)
                    continue;

                ulong bit = 1UL << ((r << 3) + c);
                if (piece.Value.Color == PieceColor.White)
                {
                    if (piece.Value.IsKing)
                        wk |= bit;
                    else
                        wm |= bit;
                }
                else
                {
                    if (piece.Value.IsKing)
                        bk |= bit;
                    else
                        bm |= bit;
                }
            }
        }

        return new BitPosition
        {
            WhiteMen = wm,
            BlackMen = bm,
            WhiteKings = wk,
            BlackKings = bk,
            Hash = state.ZobristHash,
            HalfMoveClock = state.HalfMoveClock,
            SideToMove = state.ActivePlayer
        };
    }

    /// <summary>
    /// Converts this <see cref="BitPosition"/> back into a <see cref="BoardState"/> snapshot.
    /// </summary>
    public readonly BoardState ToBoardState(int fullMoveNumber)
    {
        var state = new BoardState
        {
            ActivePlayer = SideToMove,
            HalfMoveClock = HalfMoveClock,
            FullMoveNumber = fullMoveNumber
        };

        ulong wm = WhiteMen;
        while (wm != 0)
        {
            int sq = BitOperations.TrailingZeroCount(wm);
            wm &= wm - 1;
            state.SetPiece(BitboardMasks.Positions[sq], new Piece(PieceColor.White, PieceType.Man));
        }

        ulong wk = WhiteKings;
        while (wk != 0)
        {
            int sq = BitOperations.TrailingZeroCount(wk);
            wk &= wk - 1;
            state.SetPiece(BitboardMasks.Positions[sq], new Piece(PieceColor.White, PieceType.King));
        }

        ulong bm = BlackMen;
        while (bm != 0)
        {
            int sq = BitOperations.TrailingZeroCount(bm);
            bm &= bm - 1;
            state.SetPiece(BitboardMasks.Positions[sq], new Piece(PieceColor.Black, PieceType.Man));
        }

        ulong bk = BlackKings;
        while (bk != 0)
        {
            int sq = BitOperations.TrailingZeroCount(bk);
            bk &= bk - 1;
            state.SetPiece(BitboardMasks.Positions[sq], new Piece(PieceColor.Black, PieceType.King));
        }

        state.ZobristHash = Hash;
        return state;
    }

    /// <summary>
    /// Applies a <see cref="BitMove"/> using copy-make and incremental Zobrist hash updates.
    /// Produces the exact same ZobristHash as <see cref="RuleEngine.ApplyMove"/>.
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
