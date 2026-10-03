using Checkers.Core.Models;

namespace Checkers.Core.AI;

/// <summary>
/// Common abstraction for a checkers player (Human, Random, Minimax AI, etc.).
/// </summary>
public interface IPlayer
{
    string Name { get; }

    /// <summary>
    /// Search and evaluation analysis from the player's last completed move.
    /// </summary>
    SearchAnalysis? LastAnalysis => null;

    /// <summary>
    /// Chooses a move from the available legal moves for the given state.
    /// </summary>
    ValueTask<Move> GetMoveAsync(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Chooses a move from the available legal moves for the given state,
    /// optionally reporting search progress and analysis telemetry in real time.
    /// </summary>
    ValueTask<Move> GetMoveAsync(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        IProgress<SearchAnalysis>? progress,
        CancellationToken cancellationToken = default) =>
        GetMoveAsync(state, legalMoves, cancellationToken);

    /// <summary>
    /// Chooses a move from the available legal moves for the given state,
    /// seeding in-search repetition detection with the game's Zobrist hash history.
    /// </summary>
    ValueTask<Move> GetMoveAsync(
        BoardState state,
        IReadOnlyList<Move> legalMoves,
        IProgress<SearchAnalysis>? progress,
        IReadOnlyList<ulong>? stateHashHistory,
        CancellationToken cancellationToken = default) =>
        GetMoveAsync(state, legalMoves, progress, cancellationToken);
}
