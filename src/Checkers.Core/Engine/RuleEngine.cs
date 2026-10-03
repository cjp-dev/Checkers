using Checkers.Core.Bitboards;
using Checkers.Core.Models;

namespace Checkers.Core.Engine;

/// <summary>
/// Pure rule engine for 8x8 Draughts, backed by 64-bit bitboards (<see cref="BitPosition"/> and <see cref="BitboardMoveGenerator"/>).
/// Supports both International Draughts (Flying Kings) and English Checkers (1-Step Kings).
/// </summary>
public sealed class RuleEngine : IRuleEngine
{
    public CheckersVariant Variant { get; }

    public RuleEngine(CheckersVariant variant = CheckersVariant.International)
    {
        Variant = variant;
    }

    public IReadOnlyList<Move> GetLegalMoves(BoardState state)
    {
        var bitPos = state.BitPosition;
        return BitboardMoveGenerator.GenerateMoves(in bitPos, Variant);
    }

    public bool IsLegalMove(BoardState state, Move move)
    {
        var legalMoves = GetLegalMoves(state);
        for (int i = 0; i < legalMoves.Count; i++)
        {
            var m = legalMoves[i];
            if (m.From == move.From &&
                m.To == move.To &&
                m.CapturedPositions.Count == move.CapturedPositions.Count &&
                m.CapturedPositions.SequenceEqual(move.CapturedPositions) &&
                m.Path.SequenceEqual(move.Path))
            {
                return true;
            }
        }
        return false;
    }

    public BoardState ApplyMove(BoardState state, Move move)
    {
        var piece = state.GetPiece(move.From);
        if (!piece.HasValue)
            throw new InvalidOperationException($"No piece exists at start position {move.From}.");

        var bitPos = state.BitPosition;
        var bitMove = BitMove.FromMove(move);
        var nextBitPos = bitPos.Apply(in bitMove);

        int nextFullMove = state.ActivePlayer == PieceColor.Black
            ? state.FullMoveNumber + 1
            : state.FullMoveNumber;

        return nextBitPos.ToBoardState(nextFullMove);
    }

    public (GameStatus Status, GameOverReason Reason) EvaluateGameStatus(
        BoardState state,
        IReadOnlyList<ulong>? stateHashHistory = null)
    {
        // 1. Check piece elimination
        if (state.ActivePlayer == PieceColor.White && state.WhitePiecesCount == 0)
        {
            return (GameStatus.BlackWon, GameOverReason.OpponentPiecesEliminated);
        }
        if (state.ActivePlayer == PieceColor.Black && state.BlackPiecesCount == 0)
        {
            return (GameStatus.WhiteWon, GameOverReason.OpponentPiecesEliminated);
        }

        // 2. Check no legal moves (blocked) using fast O(1) bitboard check
        var bitPos = state.BitPosition;
        if (!BitboardMoveGenerator.HasAnyLegalMove(in bitPos, Variant))
        {
            return state.ActivePlayer == PieceColor.White
                ? (GameStatus.BlackWon, GameOverReason.OpponentNoLegalMoves)
                : (GameStatus.WhiteWon, GameOverReason.OpponentNoLegalMoves);
        }

        // 3. 40-move rule without capture or promotion (80 half-moves)
        if (state.HalfMoveClock >= 80)
        {
            return (GameStatus.Draw, GameOverReason.FortyMoveRuleWithoutCaptureOrPromotion);
        }

        // 4. Threefold repetition
        if (stateHashHistory != null)
        {
            int repetitions = 0;
            ulong targetHash = state.ZobristHash;
            for (int i = 0; i < stateHashHistory.Count; i++)
            {
                if (stateHashHistory[i] == targetHash && ++repetitions >= 3)
                {
                    return (GameStatus.Draw, GameOverReason.ThreefoldRepetition);
                }
            }
        }

        return (GameStatus.InProgress, GameOverReason.None);
    }
}
