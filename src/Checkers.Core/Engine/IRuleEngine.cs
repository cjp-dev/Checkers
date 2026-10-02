using Checkers.Core.Models;

namespace Checkers.Core.Engine;

/// <summary>
/// Contract for the Checkers game rule engine.
/// </summary>
public interface IRuleEngine
{
    /// <summary>
    /// Generates all legal moves for the current active player.
    /// Strictly enforces the mandatory capture rule (returns ONLY capture moves if any exist).
    /// </summary>
    IReadOnlyList<Move> GetLegalMoves(BoardState state);

    /// <summary>
    /// Validates whether a specific move is legal for the current board state.
    /// </summary>
    bool IsLegalMove(BoardState state, Move move);

    /// <summary>
    /// Applies a move to the board state and returns the resulting new board state.
    /// </summary>
    BoardState ApplyMove(BoardState state, Move move);

    /// <summary>
    /// Evaluates if the current state is terminal (win or draw) and provides the reason.
    /// </summary>
    (GameStatus Status, GameOverReason Reason) EvaluateGameStatus(
        BoardState state,
        IReadOnlyList<ulong>? stateHashHistory = null);
}
